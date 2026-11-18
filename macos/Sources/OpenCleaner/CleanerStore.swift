import SwiftUI

@MainActor
final class CleanerStore: ObservableObject {
    @Published var items: [CleanItem] = []
    @Published var busy = false
    @Published var busyText = tr("Scanning…")
    @Published var freeText = ""
    @Published var message: String?
    @Published var confirming = false
    @Published var reviewing: CleanItem?
    @Published var previewing: URL?          // the eye on a single-file row opens the preview directly
    @Published var showHidden = false        // the Hidden files sheet
    @Published var notice: String?           // short line under the list after hiding a file
    @Published var update: Release?          // a newer release on GitHub
    @Published var updating = false
    @Published var offerUpdate = false       // show the "Update to …?" question
    @Published var tab = 0          // 0 = Clean, 1 = Disk usage

    init() { updateFree() }

    /// Asks GitHub for the latest release. Quiet on startup; `manual` also reports "up to date" and errors.
    func checkForUpdates(manual: Bool) async {
        do {
            let r = try await Updater.latest()
            if Updater.isNewer(r.version, than: Updater.currentVersion) {
                update = r
                if manual { offerUpdate = true }
            } else if manual {
                message = tr("OpenCleaner {0} is up to date.", Updater.currentVersion)
            }
        } catch {
            if manual { message = tr("Could not check for updates: {0}", error.localizedDescription) }
        }
    }

    func installUpdate() async {
        guard let r = update else { return }
        updating = true
        do { try await Updater.install(r) } catch {
            updating = false
            message = tr("Update failed: {0}", error.localizedDescription) + "\n\n" + tr("You can download it from {0}", r.page.absoluteString)
        }
    }

    @discardableResult
    func updateFree() -> Int64 {
        guard let s = FS.freeSpace() else { return 0 }
        let pct = Int((Double(s.total - s.free) / Double(max(s.total, 1)) * 100).rounded())
        freeText = tr("{0} free · {1}% used", fmt(s.free), pct)
        return s.free
    }

    var selected: [CleanItem] { items.filter { $0.selected && !$0.empty } }

    func setSelected(_ id: UUID, _ on: Bool) {
        if let i = items.firstIndex(where: { $0.id == id }) { items[i].selected = on }
        notice = nil
    }

    /// Keeps a large file out of the list from now on; nothing is deleted.
    func hide(_ item: CleanItem) {
        guard !busy else { return }
        items = Scanners.hide(items, item)
        notice = tr("{0} is hidden. It stays out of Large files until you show it again.", item.title)
    }

    /// Puts hidden files back; the rescan lists them again.
    func unhide(_ paths: Set<String>) async {
        HiddenFiles.remove(paths)
        items = Scanners.withHiddenRow(items)
        await scan()
    }

    func scan() async {
        notice = nil
        guard !busy else { return }
        busy = true; busyText = tr("Scanning…")
        items = await Task.detached(priority: .userInitiated) { Scanners.runAll() }.value
        busy = false
        updateFree()
    }

    func cleanSelected() async {
        let todo = selected
        guard !todo.isEmpty, !busy else { return }
        busy = true; busyText = tr("Cleaning…")
        let before = updateFree()
        let results = await Task.detached(priority: .userInitiated) {
            todo.map { item in (item.title, Scanners.clean(item)) }
        }.value
        busy = false
        let freed = updateFree() - before
        let failures = results.compactMap { r in r.1.error.map { "\(r.0): \($0.prefix(200))" } }
        let skipped = results.reduce(0) { $0 + $1.1.skipped }
        let inUse = skipped > 0 ? "\n\n" + tr("{0} file(s) were in use and skipped.", skipped) : ""
        if failures.isEmpty {
            message = tr("Cleaning finished") + (freed > 0 ? " · " + tr("freed {0}", fmt(freed)) : "") + inUse
        } else {
            message = tr("Some items failed:") + "\n" + failures.joined(separator: "\n") + inUse
        }
        await scan()
    }
}
