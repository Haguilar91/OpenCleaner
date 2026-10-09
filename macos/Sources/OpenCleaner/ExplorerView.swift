import SwiftUI

struct Entry: Identifiable {
    var id: URL
    var name: String
    var isDir: Bool
    var size: Int64?
}

@MainActor
final class ExplorerModel: ObservableObject {
    @Published var root = URL(fileURLWithPath: home)
    @Published var path = URL(fileURLWithPath: home)
    @Published var entries: [Entry] = []
    @Published var measuring = false
    @Published var error: String?
    @Published var toTrash: URL?
    private var gen = 0

    var total: Int64 { entries.reduce(0) { $0 + ($1.size ?? 0) } }
    var maxSize: Int64 { max(entries.compactMap(\.size).max() ?? 1, 1) }
    var statusText: String {
        var s = measuring ? "Measuring… " : ""
        s += "Total \(fmt(total))"
        if let e = error { s += " · \(e)" }
        return s
    }
    var canGoUp: Bool { path.standardizedFileURL.path != root.standardizedFileURL.path }

    func setRoot(_ url: URL) { root = url; go(url) }
    func up() { go(path.deletingLastPathComponent()) }
    func reload() { go(path) }

    func go(_ url: URL) {
        path = url
        gen += 1
        let g = gen
        let urls = FS.children(url)
        entries = urls.map { Entry(id: $0, name: $0.lastPathComponent, isDir: FS.isRealDir($0)) }
        error = nil
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
        entries.sort { ($0.size ?? -1) > ($1.size ?? -1) }
    }

    private func finish(_ g: Int) { if g == gen { measuring = false } }

    func trash(_ url: URL) {
        do {
            try FileManager.default.trashItem(at: url, resultingItemURL: nil)
            reload()
        } catch {
            self.error = error.localizedDescription
        }
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
            }
            .padding(.horizontal, 24).padding(.top, 12).padding(.bottom, 4)
            Text(model.statusText)
                .font(.caption).foregroundStyle(.secondary)
                .frame(maxWidth: .infinity, alignment: .leading)
                .padding(.horizontal, 28).padding(.bottom, 8)
            ScrollView {
                Card {
                    LazyVStack(spacing: 0) {
                        ForEach(Array(model.entries.enumerated()), id: \.element.id) { i, e in
                            if i > 0 { Divider() }
                            row(e)
                        }
                    }
                }
                .padding(.horizontal, 24).padding(.bottom, 16)
            }
        }
        .onAppear { if model.entries.isEmpty { model.reload() } }
        .alert("Move to Trash?", isPresented: Binding(get: { model.toTrash != nil },
                                                       set: { if !$0 { model.toTrash = nil } })) {
            Button("Move to Trash", role: .destructive) {
                if let u = model.toTrash { model.trash(u); store.updateFree() }
                model.toTrash = nil
            }
            Button("Cancel", role: .cancel) { model.toTrash = nil }
        } message: { Text(model.toTrash?.path ?? "") }
    }

    @ViewBuilder private func row(_ e: Entry) -> some View {
        HStack(spacing: 12) {
            Button { if e.isDir { model.go(e.id) } } label: {
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
            Button { model.toTrash = e.id } label: { Image(systemName: "trash") }
                .buttonStyle(.borderless).help("Move to Trash")
        }
        .padding(.horizontal, 14).padding(.vertical, 8)
    }
}
