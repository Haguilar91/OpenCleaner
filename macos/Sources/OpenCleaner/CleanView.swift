import AppKit
import SwiftUI

struct CleanView: View {
    @EnvironmentObject var store: CleanerStore

    private var groups: [String] {
        var seen: [String] = []
        for i in store.items where !seen.contains(i.group) { seen.append(i.group) }
        return seen
    }

    var body: some View {
        VStack(spacing: 0) {
            ZStack {
                ScrollView {
                    VStack(alignment: .leading, spacing: 20) {
                        ForEach(groups, id: \.self) { g in
                            groupView(g)
                        }
                    }
                    .frame(maxWidth: 680)
                    .padding(.horizontal, 24)
                    .padding(.vertical, 16)
                    .frame(maxWidth: .infinity)
                }
                if store.busy {
                    VStack(spacing: 10) {
                        ProgressView()
                        Text(store.busyText).foregroundStyle(.secondary)
                    }
                    .padding(24)
                    .background(.regularMaterial, in: RoundedRectangle(cornerRadius: 12))
                }
            }
            Divider()
            HStack {
                Text(store.notice ?? summary).foregroundStyle(.secondary).lineLimit(2)
                Spacer()
                Button(tr("Clean selected")) { store.confirming = true }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(store.selected.isEmpty || store.busy)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 12)
            .background(.bar)
        }
        .sheet(item: $store.reviewing) { ReviewSheet(item: $0) }
        .sheet(item: Binding(get: { store.previewing.map(Identified.init) }, set: { store.previewing = $0?.url })) {
            PreviewSheet(url: $0.url)
        }
        .sheet(isPresented: $store.showHidden) { HiddenSheet(store: store) }
        .alert(tr("Delete selected items?"), isPresented: $store.confirming) {
            Button(tr("Clean"), role: .destructive) { Task { await store.cleanSelected() } }
            Button(tr("Cancel"), role: .cancel) {}
        } message: {
            Text(tr("This cannot be undone.") + "\n\n" + store.selected.map {
                "\u{2022} \($0.title) (\(fmt($0.size)))" + ($0.note.isEmpty ? "" : "\n   \($0.note)")
            }.joined(separator: "\n"))
        }
        .alert("OpenCleaner", isPresented: Binding(get: { store.message != nil },
                                                   set: { if !$0 { store.message = nil } })) {
            Button(tr("OK")) { store.message = nil }
        } message: { Text(store.message ?? "") }
    }

    private func groupView(_ g: String) -> some View {
        let items = store.items.filter { $0.group == g }
        return VStack(alignment: .leading, spacing: 6) {
            Text(g).font(.system(size: 13, weight: .bold)).padding(.leading, 4)
            Card {
                VStack(spacing: 0) {
                    ForEach(Array(items.enumerated()), id: \.element.id) { i, item in
                        if i > 0 { Divider() }
                        row(item)
                    }
                }
            }
        }
    }

    private var summary: String {
        let sel = store.selected
        let known = sel.reduce(Int64(0)) { $0 + ($1.size ?? 0) }
        return tr("{0} selected · {1}", sel.count, fmt(known)) + (sel.contains { $0.size == nil } ? " " + tr("+ more") : "")
    }

    @ViewBuilder private func row(_ item: CleanItem) -> some View {
        if item.kind == "hidden" { hiddenRow(item) } else { itemRow(item) }
    }

    /// "Hidden files (N)": clicking it lists the hidden large files so they can be shown again.
    private func hiddenRow(_ item: CleanItem) -> some View {
        Button { store.showHidden = true } label: {
            HStack(spacing: 12) {
                LucideIcon(.hide).foregroundStyle(.secondary).frame(width: 20)
                VStack(alignment: .leading, spacing: 2) {
                    Text(item.title)
                    Text(item.subtitle).font(.caption).foregroundStyle(.secondary).lineLimit(2)
                }
                Spacer()
                Text(tr("Show")).foregroundStyle(.secondary)
                LucideIcon(.chevronRight).foregroundStyle(.secondary)
            }
            .padding(.horizontal, 14)
            .padding(.vertical, 10)
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
    }

    /// The eye: a single file opens straight in the preview, anything else lists its files.
    private func openEye(_ item: CleanItem) {
        if item.paths.count == 1, let f = item.paths.first, !FS.isRealDir(f) { store.previewing = f }
        else { store.reviewing = item }
    }

    @ViewBuilder private func itemRow(_ item: CleanItem) -> some View {
        HStack(spacing: 12) {
            if item.empty {
                LucideIcon(item.failed ? .x : .check)
                    .foregroundStyle(item.failed ? Color.red : Color.green)
                    .frame(width: 20)
            } else {
                Toggle("", isOn: Binding(get: { item.selected },
                                         set: { store.setSelected(item.id, $0) }))
                    .toggleStyle(.checkbox)
                    .labelsHidden()
                    .frame(width: 20)
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(item.title)
                Text(item.subtitle)
                    .font(.caption)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }
            Spacer()
            if item.empty {
                if let url = item.settingsURL {
                    Button(tr("Open Settings")) { NSWorkspace.shared.open(url) }
                }
                Text(item.failed ? tr("Scan failed") : tr("status|Clean") + " \u{2713}")
                    .foregroundStyle(item.failed ? Color.red : Color.green)
            } else {
                Text(fmt(item.size)).foregroundStyle(.secondary).monospacedDigit()
                if !item.paths.isEmpty {
                    let single = item.paths.count == 1 && !FS.isRealDir(item.paths[0])
                    Button { openEye(item) } label: { LucideIcon(.review) }
                        .buttonStyle(.borderless).help(single ? tr("Preview") : tr("Review files"))
                }
                if item.kind == "big" {
                    Button { store.hide(item) } label: { LucideIcon(.hide) }
                        .buttonStyle(.borderless).help(tr("Hide this file"))
                }
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}

final class HiddenModel: ObservableObject {
    @Published var back: Set<String> = []     // ticked to show again
    let paths = HiddenFiles.all

    func toggle(_ p: String) { if back.contains(p) { back.remove(p) } else { back.insert(p) } }

    func detail(_ p: String) -> String {
        let dir = (p as NSString).deletingLastPathComponent
        let shown: String = dir.hasPrefix(home) ? "~" + String(dir.dropFirst(home.count)) : dir
        let attrs = try? FileManager.default.attributesOfItem(atPath: p)
        let size = (attrs?[.size] as? NSNumber)?.int64Value
        let what: String = size.map { fmt($0) } ?? tr("no longer exists")
        return shown + " \u{00B7} " + what
    }
}

/// Lists the hidden large files; the ticked ones are shown again after a rescan.
struct HiddenSheet: View {
    @ObservedObject var store: CleanerStore
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model = HiddenModel()

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(tr("Hidden files")).font(.headline)
            Text(tr("These files are left out of Large files. Tick the ones to show again."))
                .font(.caption).foregroundStyle(.secondary)
            ScrollView {
                LazyVStack(alignment: .leading, spacing: 0) {
                    ForEach(model.paths, id: \.self) { p in
                        hiddenLine(p)
                        Divider()
                    }
                }
            }
            .frame(height: 260)
            HStack {
                Spacer()
                Button(tr("Close")) { dismiss() }.keyboardShortcut(.cancelAction)
                Button(tr("Show again")) {
                    let back = model.back
                    dismiss()
                    Task { await store.unhide(back) }
                }
                .buttonStyle(.borderedProminent)
                .disabled(model.back.isEmpty)
            }
        }
        .padding(20)
        .frame(width: 520)
    }

    private func hiddenLine(_ p: String) -> some View {
        HStack(spacing: 10) {
            Toggle("", isOn: Binding(get: { model.back.contains(p) }, set: { _ in model.toggle(p) }))
                .toggleStyle(.checkbox).labelsHidden()
            VStack(alignment: .leading, spacing: 2) {
                Text((p as NSString).lastPathComponent).lineLimit(1)
                Text(model.detail(p)).font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }
            Spacer()
        }
        .padding(.vertical, 5)
    }
}
