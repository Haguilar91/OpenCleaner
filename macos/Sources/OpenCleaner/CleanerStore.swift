import SwiftUI

@MainActor
final class CleanerStore: ObservableObject {
    @Published var items: [CleanItem] = []
    @Published var busy = false
    @Published var busyText = "Scanning…"
    @Published var freeText = ""
    @Published var message: String?
    @Published var confirming = false
    @Published var reviewing: CleanItem?
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
                message = "OpenCleaner \(Updater.currentVersion) is up to date."
            }
        } catch {
            if manual { message = "Could not check for updates: \(error.localizedDescription)" }
        }
    }

    func installUpdate() async {
        guard let r = update else { return }
        updating = true
        do { try await Updater.install(r) } catch {
            updating = false
            message = "Update failed: \(error.localizedDescription)\n\nYou can download it from \(r.page.absoluteString)"
        }
    }

    @discardableResult
    func updateFree() -> Int64 {
        guard let s = FS.freeSpace() else { return 0 }
        let pct = Int((Double(s.total - s.free) / Double(max(s.total, 1)) * 100).rounded())
        freeText = "\(fmt(s.free)) free · \(pct)% used"
        return s.free
    }

    var selected: [CleanItem] { items.filter { $0.selected && !$0.empty } }

    func setSelected(_ id: UUID, _ on: Bool) {
        if let i = items.firstIndex(where: { $0.id == id }) { items[i].selected = on }
    }

    func scan() async {
        guard !busy else { return }
        busy = true; busyText = "Scanning…"
        items = await Task.detached(priority: .userInitiated) { Scanners.runAll() }.value
        busy = false
        updateFree()
    }

    func cleanSelected() async {
        let todo = selected
        guard !todo.isEmpty, !busy else { return }
        busy = true; busyText = "Cleaning…"
        let before = updateFree()
        let failures: [String] = await Task.detached(priority: .userInitiated) {
            todo.compactMap { item in Scanners.clean(item).map { "\(item.title): \($0.prefix(200))" } }
        }.value
        busy = false
        let freed = updateFree() - before
        if failures.isEmpty {
            message = "Cleaning finished" + (freed > 0 ? " · freed \(fmt(freed))" : "")
        } else {
            message = "Some items failed:\n" + failures.joined(separator: "\n")
        }
        await scan()
    }
}
