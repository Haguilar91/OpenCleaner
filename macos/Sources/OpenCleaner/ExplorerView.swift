import SwiftUI

struct Entry: Identifiable {
    var id: URL
    var name: String
    var isDir: Bool
    var size: Int64?
    var modified: Date?
}

/// What deleting from Disk usage does. Uninstall is always permanent and does not use this.
enum DeleteMode: String, CaseIterable, Identifiable {
    case trash = "Move to Trash", permanent = "Delete permanently"
    var id: String { rawValue }
}

enum SortMode: String, CaseIterable, Identifiable {
    case size = "Biggest first", oldest = "Oldest first", newest = "Newest first", type = "File type"
    var id: String { rawValue }
}

@MainActor
final class ExplorerModel: ObservableObject {
    @Published var root = URL(fileURLWithPath: home)
    @Published var path = URL(fileURLWithPath: home)
    @Published var entries: [Entry] = []
    @Published var measuring = false
    @Published var measured = 0
    @Published var itemCount = 0
    @Published var error: String?
    @Published var pendingDelete: [URL] = []      // waiting for confirmation
    @Published var selected: Set<URL> = []        // ticked in the list or right-clicked in the map
    @Published var deleteMode = DeleteMode(rawValue: UserDefaults.standard.string(forKey: "deleteMode") ?? "")
        ?? .trash {
        didSet { UserDefaults.standard.set(deleteMode.rawValue, forKey: "deleteMode") }
    }
    @Published var preview: URL?
    @Published var mapMode = false
    @Published var sort = SortMode.size
    @Published var topN = 0          // 0 = show all
    private var gen = 0

    var total: Int64 { entries.reduce(0) { $0 + ($1.size ?? 0) } }
    var maxSize: Int64 { max(visible.compactMap(\.size).max() ?? 1, 1) }

    /// Entries in the chosen order, cut to the top N (list and map both use this).
    var visible: [Entry] {
        var list = entries
        switch sort {
        case .size: list.sort { ($0.size ?? -1) > ($1.size ?? -1) }
        case .oldest: list.sort { ($0.modified ?? .distantFuture) < ($1.modified ?? .distantFuture) }
        case .newest: list.sort { ($0.modified ?? .distantPast) > ($1.modified ?? .distantPast) }
        case .type:
            list.sort { a, b in
                let ka = Kind.of(a).order, kb = Kind.of(b).order
                return ka != kb ? ka < kb : (a.size ?? -1) > (b.size ?? -1)
            }
        }
        return topN > 0 ? Array(list.prefix(topN)) : list
    }
    var statusText: String {
        var s = measuring ? "Measuring \(percent)% (\(measured) of \(itemCount) items) \u{00B7} " : ""
        s += "Total \(fmt(total))"
        if let e = error { s += " · \(e)" }
        return s
    }
    var percent: Int { itemCount == 0 ? 100 : min(100, measured * 100 / itemCount) }
    var canGoUp: Bool { path.standardizedFileURL.path != root.standardizedFileURL.path }

    func setRoot(_ url: URL) { root = url; go(url) }
    func up() { go(path.deletingLastPathComponent()) }
    func reload() { go(path) }

    var selectedSize: Int64 { entries.filter { selected.contains($0.id) }.reduce(0) { $0 + ($1.size ?? 0) } }

    func toggleSelected(_ url: URL) {
        if selected.contains(url) { selected.remove(url) } else { selected.insert(url) }
    }

    func go(_ url: URL) {
        path = url
        selected = []
        gen += 1
        let g = gen
        let urls = FS.children(url)
        entries = urls.map {
            Entry(id: $0, name: $0.lastPathComponent, isDir: FS.isRealDir($0),
                  modified: (try? $0.resourceValues(forKeys: [.contentModificationDateKey]))?.contentModificationDate)
        }
        error = nil
        measured = 0
        itemCount = urls.count
        guard !urls.isEmpty else { measuring = false; return }
        measuring = true
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
        guard g == gen, let i = entries.firstIndex(where: { $0.id == url }) else { return }
        entries[i].size = size
        measured += 1
    }

    private func finish(_ g: Int) { if g == gen { measuring = false } }

    /// Deletes the confirmed items: to the Trash, or permanently (only inside the home folder).
    func deletePending() {
        let todo = pendingDelete
        pendingDelete = []
        var failed: [String] = []
        for url in todo {
            do {
                if deleteMode == .trash {
                    try FileManager.default.trashItem(at: url, resultingItemURL: nil)
                } else {
                    try FS.safeRemove(url)
                }
            } catch {
                failed.append("\(url.lastPathComponent): \(error.localizedDescription)")
            }
        }
        selected.subtract(todo)
        error = failed.isEmpty ? nil : "Could not delete " + failed.joined(separator: "; ")
        reload()
    }
}

struct ExplorerView: View {
    @EnvironmentObject var store: CleanerStore
    @ObservedObject var model: ExplorerModel

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                Button { model.up() } label: { Image(systemName: "chevron.up") }
                    .disabled(!model.canGoUp)
                Button("Home") { model.setRoot(URL(fileURLWithPath: home)) }
                Button("System (/)") { model.setRoot(URL(fileURLWithPath: "/")) }
                Text(model.path.path)
                    .lineLimit(1).truncationMode(.head).foregroundStyle(.secondary)
                Spacer()
                Picker("Sort", selection: $model.sort) {
                    ForEach(SortMode.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.menu).frame(width: 140).help("Sort by size, age or type")
                Picker("Show", selection: $model.topN) {
                    Text("All").tag(0)
                    ForEach([5, 10, 50, 100], id: \.self) { Text("Top \($0)").tag($0) }
                }
                .pickerStyle(.menu).frame(width: 100).help("Show only the first items of the sort")
                Picker("", selection: $model.mapMode) {
                    Image(systemName: "list.bullet").tag(false)
                    Image(systemName: "square.grid.2x2").tag(true)
                }
                .pickerStyle(.segmented).labelsHidden().frame(width: 80)
                .help("List or map")
                Picker("", selection: $model.deleteMode) {
                    ForEach(DeleteMode.allCases) { Text($0.rawValue).tag($0) }
                }
                .pickerStyle(.menu).labelsHidden().frame(width: 160)
                .help("What Delete does in Disk usage (uninstalling is always permanent)")
            }
            .padding(.horizontal, 24).padding(.top, 12).padding(.bottom, 4)
            Text(model.statusText)
                .font(.caption).foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28).padding(.bottom, model.measuring ? 4 : 8)
            if model.measuring {
                ProgressView(value: Double(model.measured), total: Double(max(model.itemCount, 1)))
                    .padding(.horizontal, 28).padding(.bottom, 8)
            }
            if model.mapMode {
                MapView(model: model)
            } else {
            ScrollView {
                Card {
                    LazyVStack(spacing: 0) {
                        ForEach(Array(model.visible.enumerated()), id: \.element.id) { i, e in
                            if i > 0 { Divider() }
                            row(e)
                        }
                    }
                }
                .padding(.horizontal, 24).padding(.bottom, 16)
            }
            }
            if !model.selected.isEmpty {
                Divider()
                HStack {
                    Text("\(model.selected.count) selected \u{00B7} \(fmt(model.selectedSize))")
                        .foregroundStyle(.secondary)
                    Button("Clear") { model.selected = [] }.buttonStyle(.borderless)
                    Spacer()
                    Button(model.deleteMode == .trash ? "Move selected to Trash" : "Delete selected permanently",
                           role: .destructive) {
                        model.pendingDelete = model.visible.map(\.id).filter { model.selected.contains($0) }
                            + model.selected.filter { u in !model.visible.contains { $0.id == u } }
                    }
                    .buttonStyle(.borderedProminent)
                }
                .padding(.horizontal, 24).padding(.vertical, 10)
                .background(.bar)
            }
        }
        .onAppear { if model.entries.isEmpty { model.reload() } }
        .sheet(item: Binding(get: { model.preview.map(Identified.init) }, set: { model.preview = $0?.url })) { f in
            PreviewSheet(url: f.url, deleteTitle: model.deleteMode.rawValue) { model.pendingDelete = [$0] }
        }
        .alert(confirmTitle, isPresented: Binding(get: { !model.pendingDelete.isEmpty },
                                                  set: { if !$0 { model.pendingDelete = [] } })) {
            Button(model.deleteMode.rawValue, role: .destructive) {
                model.deletePending()
                store.updateFree()
            }
            Button("Cancel", role: .cancel) { model.pendingDelete = [] }
        } message: { Text(confirmMessage) }
    }

    private var confirmTitle: String {
        let n = model.pendingDelete.count
        let what = n == 1 ? "\"\(model.pendingDelete[0].lastPathComponent)\"" : "\(n) items"
        return model.deleteMode == .trash ? "Move \(what) to the Trash?" : "Delete \(what) permanently?"
    }

    private var confirmMessage: String {
        let names = model.pendingDelete.prefix(12).map { $0.path }.joined(separator: "\n")
        let more = model.pendingDelete.count > 12 ? "\n…and \(model.pendingDelete.count - 12) more" : ""
        let note = model.deleteMode == .trash
            ? "You can put them back from the Trash."
            : "This cannot be undone: nothing goes to the Trash."
        return note + "\n\n" + names + more
    }

    @ViewBuilder private func row(_ e: Entry) -> some View {
        HStack(spacing: 12) {
            Toggle("", isOn: Binding(get: { model.selected.contains(e.id) }, set: { _ in model.toggleSelected(e.id) }))
                .toggleStyle(.checkbox).labelsHidden()
            Button { if e.isDir { model.go(e.id) } else { model.preview = e.id } } label: {
                HStack(spacing: 10) {
                    Image(systemName: e.isDir ? "folder.fill" : "doc")
                        .foregroundStyle(e.isDir ? Color.accentColor : Color.secondary)
                        .frame(width: 20)
                    Text(e.name).lineLimit(1)
                    Spacer(minLength: 8)
                }
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            ProgressView(value: Double(e.size ?? 0), total: Double(model.maxSize))
                .frame(width: 100)
            Text(e.size.map { fmt($0) } ?? "…")
                .monospacedDigit().foregroundStyle(.secondary)
                .frame(width: 84, alignment: .trailing)
            Button { model.pendingDelete = [e.id] } label: { Image(systemName: "trash") }
                .buttonStyle(.borderless).help(model.deleteMode.rawValue)
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
    }
}
