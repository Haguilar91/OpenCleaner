import SwiftUI
import AppKit

struct AppInfo: Identifiable {
    var id: URL                 // the .app bundle
    var name: String
    var bundleID: String
    var version: String
    var size: Int64?
    var lastUsed: Date?
}

enum AppSort: String, CaseIterable, Identifiable {
    case size = "Biggest first", leastUsed = "Least recently used", recent = "Recently used", name = "Name"
    var id: String { rawValue }
    var label: String { tr(rawValue) }
}

/// One thing the uninstall would delete permanently: the app itself or a leftover file or folder.
struct PlanItem: Identifiable {
    var id: URL
    var title: String
    var detail: String
    var size: Int64
    var selected: Bool
}

@MainActor
final class AppsModel: ObservableObject {
    @Published var apps: [AppInfo] = []
    @Published var loading = false
    @Published var measured = 0
    @Published var sort = AppSort.size
    @Published var topN = 0               // 0 = show all
    @Published var uninstalling: AppInfo?
    @Published var plan: [PlanItem] = []
    @Published var planBusy = false
    @Published var message: String?
    private var gen = 0

    var percent: Int { apps.isEmpty ? 100 : min(100, measured * 100 / apps.count) }
    var total: Int64 { apps.reduce(0) { $0 + ($1.size ?? 0) } }

    var statusText: String {
        var s = loading ? tr("Measuring {0}% ({1} of {2} apps)", percent, measured, apps.count) + " \u{00B7} " : ""
        s += tr("{0} apps · {1}", apps.count, fmt(total))
        return s
    }

    /// Apps in the chosen order, cut to the top N.
    var visible: [AppInfo] {
        var list = apps
        switch sort {
        case .size: list.sort { ($0.size ?? -1) > ($1.size ?? -1) }
        case .leastUsed: list.sort { ($0.lastUsed ?? .distantPast) < ($1.lastUsed ?? .distantPast) }
        case .recent: list.sort { ($0.lastUsed ?? .distantPast) > ($1.lastUsed ?? .distantPast) }
        case .name: list.sort { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
        }
        return topN > 0 ? Array(list.prefix(topN)) : list
    }

    var planSelectedSize: Int64 { plan.filter(\.selected).reduce(0) { $0 + $1.size } }

    // MARK: scanning

    func load() {
        gen += 1
        let g = gen
        let found = Self.findApps()
        apps = found
        measured = 0
        loading = !found.isEmpty
        guard !found.isEmpty else { return }
        let urls = found.map(\.id)
        Task.detached { [weak self] in
            await withTaskGroup(of: (URL, Int64).self) { group in
                var it = urls.makeIterator()
                for _ in 0..<4 { if let u = it.next() { group.addTask { (u, FS.size(u)) } } }
                while let (u, s) = await group.next() {
                    await self?.setSize(g, u, s)
                    if let n = it.next() { group.addTask { (n, FS.size(n)) } }
                }
            }
            await self?.finish(g)
        }
    }

    private func setSize(_ g: Int, _ url: URL, _ size: Int64) {
        guard g == gen, let i = apps.firstIndex(where: { $0.id == url }) else { return }
        apps[i].size = size
        measured += 1
    }

    private func finish(_ g: Int) { if g == gen { loading = false } }

    /// Apps in /Applications and ~/Applications. Apple's own apps are left out: they cannot be removed.
    nonisolated static func findApps() -> [AppInfo] {
        let dirs = [URL(fileURLWithPath: "/Applications"), URL(fileURLWithPath: home + "/Applications")]
        var out: [AppInfo] = []
        for dir in dirs {
            for url in FS.children(dir) where url.pathExtension == "app" {
                guard let bundle = Bundle(url: url), let id = bundle.bundleIdentifier else { continue }
                if id.hasPrefix("com.apple.") || id == Bundle.main.bundleIdentifier { continue }
                let name = (bundle.object(forInfoDictionaryKey: "CFBundleDisplayName") as? String)
                    ?? (bundle.object(forInfoDictionaryKey: "CFBundleName") as? String)
                    ?? url.deletingPathExtension().lastPathComponent
                let version = bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? ""
                let used = NSMetadataItem(url: url)?.value(forAttribute: "kMDItemLastUsedDate") as? Date
                out.append(AppInfo(id: url, name: name, bundleID: id, version: version, size: nil, lastUsed: used))
            }
        }
        return out
    }

    // MARK: uninstall

    /// Builds the review list: the app, then files it left in ~/Library.
    func startUninstall(_ app: AppInfo) {
        if !NSRunningApplication.runningApplications(withBundleIdentifier: app.bundleID).isEmpty {
            message = tr("{0} is running. Quit it first, then try again.", app.name)
            return
        }
        uninstalling = app
        planBusy = true
        plan = [PlanItem(id: app.id, title: app.name + ".app", detail: app.id.deletingLastPathComponent().path,
                         size: app.size ?? 0, selected: true)]
        Task.detached { [weak self] in
            let items = Self.leftovers(for: app)
            await self?.setLeftovers(app, items)
        }
    }

    private func setLeftovers(_ app: AppInfo, _ items: [PlanItem]) {
        guard uninstalling?.id == app.id else { return }
        plan += items
        planBusy = false
    }

    func toggle(_ id: URL) {
        if let i = plan.firstIndex(where: { $0.id == id }) { plan[i].selected.toggle() }
    }

    /// Files an app leaves in ~/Library. Matches are strict: the bundle id (or id.something), or the
    /// app's exact name in Application Support / Caches / Logs.
    nonisolated static func leftovers(for app: AppInfo) -> [PlanItem] {
        let lib = URL(fileURLWithPath: home).appendingPathComponent("Library")
        let appFile = app.id.deletingPathExtension().lastPathComponent
        let names: Set<String> = [app.name, appFile]
        // (folder, tick by default?) Caches, prefs and logs are safe; app data may hold documents or saves.
        let folders: [(String, Bool)] = [
            ("Caches", true), ("Preferences", true), ("Logs", true), ("Saved Application State", true),
            ("HTTPStorages", true), ("WebKit", true), ("Cookies", true), ("LaunchAgents", true),
            ("Application Support", false), ("Containers", false), ("Group Containers", false),
            ("Application Scripts", false),
        ]
        var items: [PlanItem] = []
        for (folder, ticked) in folders {
            let dir = lib.appendingPathComponent(folder)
            for c in FS.children(dir) {
                let n = c.lastPathComponent
                var match = n == app.bundleID || n.hasPrefix(app.bundleID + ".")
                if folder == "Group Containers" { match = n.contains(app.bundleID) }
                if ["Application Support", "Caches", "Logs"].contains(folder), names.contains(n) { match = true }
                guard match else { continue }
                items.append(PlanItem(id: c, title: n, detail: "~/Library/\(folder)", size: FS.size(c), selected: ticked))
            }
        }
        return items.sorted { $0.size > $1.size }
    }

    /// Permanently deletes the ticked items (they do NOT go to the Trash). The app must be in an
    /// Applications folder and leftovers inside ~/Library; anything else is refused.
    func confirmUninstall() {
        guard let app = uninstalling else { return }
        let todo = plan.filter(\.selected)
        uninstalling = nil
        var failed: [String] = []
        var needAdmin: [PlanItem] = []
        let apps = ["/Applications/", home + "/Applications/"]
        for item in todo {
            let p = item.id.standardizedFileURL.path
            let okApp = item.id == app.id && apps.contains { p.hasPrefix($0) }
            let okLeft = item.id != app.id && p.hasPrefix(home + "/Library/")
            guard okApp || okLeft else { failed.append("\(item.title): " + tr("outside the allowed folders")); continue }
            do { try FileManager.default.removeItem(at: item.id) }   // removes a symlink itself, not its target
            catch where Self.needsAdmin(error) { needAdmin.append(item) }
            catch { failed.append("\(item.title): \(error.localizedDescription)") }
        }
        // Apps installed by a .pkg (Microsoft Office, for example) belong to root: ask macOS for the
        // administrator password, then remove exactly these already-checked paths.
        if !needAdmin.isEmpty, let err = Self.adminRemove(needAdmin.map { $0.id.standardizedFileURL.path }) {
            failed += needAdmin.map { "\($0.title): \(err)" }
        }
        message = failed.isEmpty
            ? tr("{0} was deleted permanently ({1} freed).", app.name, fmt(todo.reduce(0) { $0 + $1.size }))
            : tr("Some items could not be deleted:") + "\n" + failed.joined(separator: "\n")
        load()
    }

    /// True for "permission denied" style errors, which an administrator can get past.
    nonisolated static func needsAdmin(_ error: Error) -> Bool {
        let e = error as NSError
        if e.domain == NSCocoaErrorDomain && [513, 640].contains(e.code) { return true }   // no permission / read-only
        if let u = e.userInfo[NSUnderlyingErrorKey] as? NSError, u.domain == NSPOSIXErrorDomain,
           [Int(EACCES), Int(EPERM)].contains(u.code) { return true }
        return e.domain == NSPOSIXErrorDomain && [Int(EACCES), Int(EPERM)].contains(e.code)
    }

    /// `rm -rf` the given paths through the macOS administrator prompt. Returns an error message, or nil.
    nonisolated static func adminRemove(_ paths: [String]) -> String? {
        let quoted = paths.map { "'" + $0.replacingOccurrences(of: "'", with: "'\\''") + "'" }.joined(separator: " ")
        let shell = "/bin/rm -rf " + quoted
        let escaped = shell.replacingOccurrences(of: "\\", with: "\\\\").replacingOccurrences(of: "\"", with: "\\\"")
        let source = "do shell script \"" + escaped + "\" with administrator privileges"
        var err: NSDictionary?
        NSAppleScript(source: source)?.executeAndReturnError(&err)
        guard let err else { return nil }
        return (err[NSAppleScript.errorMessage] as? String) ?? tr("cancelled at the administrator prompt")
    }
}

struct AppsView: View {
    @ObservedObject var model: AppsModel
    @EnvironmentObject var store: CleanerStore

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 10) {
                Text(model.statusText).font(.caption).foregroundStyle(.secondary)
                Spacer()
                Picker(tr("Sort"), selection: $model.sort) {
                    ForEach(AppSort.allCases) { Text($0.label).tag($0) }
                }
                .pickerStyle(.menu).labelsHidden().frame(width: 170).help(tr("Sort by size, last use or name"))
                Picker(tr("Show"), selection: $model.topN) {
                    Text(tr("All")).tag(0)
                    ForEach([5, 10, 50, 100], id: \.self) { Text(tr("Top {0}", $0)).tag($0) }
                }
                .pickerStyle(.menu).labelsHidden().frame(width: 120).help(tr("Show only the first items of the sort"))
            }
            .padding(.horizontal, 28).padding(.vertical, 8)
            if model.loading {
                ProgressView(value: Double(model.measured), total: Double(max(model.apps.count, 1)))
                    .padding(.horizontal, 28).padding(.bottom, 8)
            }
            ScrollView {
                Card {
                    LazyVStack(spacing: 0) {
                        ForEach(Array(model.visible.enumerated()), id: \.element.id) { i, a in
                            if i > 0 { Divider() }
                            row(a)
                        }
                    }
                }
                .padding(.horizontal, 24).padding(.bottom, 16)
            }
        }
        .onAppear { if model.apps.isEmpty { model.load() } }
        .sheet(item: $model.uninstalling) { _ in UninstallSheet(model: model) }
        .alert("OpenCleaner", isPresented: Binding(get: { model.message != nil },
                                                   set: { if !$0 { model.message = nil } })) {
            Button(tr("OK")) { model.message = nil; store.updateFree() }
        } message: { Text(model.message ?? "") }
    }

    private func lastUsedText(_ d: Date?) -> String {
        guard let d else { return tr("Last used: unknown") }
        return tr("Last used {0}", RelativeDateTimeFormatter().localizedString(for: d, relativeTo: Date()))
    }

    @ViewBuilder private func row(_ a: AppInfo) -> some View {
        HStack(spacing: 12) {
            Image(nsImage: NSWorkspace.shared.icon(forFile: a.id.path))
                .resizable().frame(width: 30, height: 30)
            VStack(alignment: .leading, spacing: 2) {
                Text(a.name).lineLimit(1)
                Text(a.version.isEmpty ? a.bundleID : "\(a.version) \u{00B7} \(a.bundleID)")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer(minLength: 8)
            Text(lastUsedText(a.lastUsed)).font(.caption).foregroundStyle(.secondary)
                .frame(width: 170, alignment: .trailing)
            Text(a.size.map { fmt($0) } ?? "\u{2026}")
                .monospacedDigit().foregroundStyle(.secondary)
                .frame(width: 84, alignment: .trailing)
            Button { model.startUninstall(a) } label: { Image(systemName: "trash") }
                .buttonStyle(.borderless).help(tr("Uninstall…"))
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
    }
}

/// Review before uninstalling: the app and its leftovers, each with a checkbox. Nothing happens until Delete permanently.
struct UninstallSheet: View {
    @ObservedObject var model: AppsModel

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(tr("Uninstall {0}?", model.uninstalling?.name ?? "")).font(.headline)
            Label(tr("Ticked items are deleted permanently. They do not go to the Trash, so this cannot be undone."), systemImage: "exclamationmark.triangle.fill")
                .font(.callout).foregroundStyle(.red)
            Text(tr("Items that may hold your data (Application Support, containers) start unticked. Apps installed for all users (like Microsoft Office) ask for your administrator password."))
                .font(.caption).foregroundStyle(.secondary)
            ScrollView {
                VStack(spacing: 0) {
                    ForEach(model.plan) { item in
                        HStack(spacing: 10) {
                            Toggle("", isOn: Binding(get: { item.selected }, set: { _ in model.toggle(item.id) }))
                                .toggleStyle(.checkbox).labelsHidden()
                            VStack(alignment: .leading, spacing: 1) {
                                Text(item.title).lineLimit(1)
                                Text(item.detail).font(.caption).foregroundStyle(.secondary).lineLimit(1)
                            }
                            Spacer()
                            Text(fmt(item.size)).monospacedDigit().foregroundStyle(.secondary)
                        }
                        .padding(.vertical, 5)
                        Divider()
                    }
                    if model.planBusy {
                        HStack { ProgressView().controlSize(.small); Text(tr("Looking for leftover files…")).font(.caption) }
                            .padding(.top, 8)
                    }
                }
            }
            .frame(height: 300)
            HStack {
                Text(tr("{0} selected · {1}", model.plan.filter(\.selected).count, fmt(model.planSelectedSize)))
                    .foregroundStyle(.secondary)
                Spacer()
                Button(tr("Cancel")) { model.uninstalling = nil }.keyboardShortcut(.cancelAction)
                Button(tr("Delete permanently"), role: .destructive) { model.confirmUninstall() }
                    .disabled(model.planBusy || model.plan.allSatisfy { !$0.selected })
            }
        }
        .padding(20)
        .frame(width: 560)
    }
}
