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
                List {
                    ForEach(groups, id: \.self) { g in
                        Section(g) {
                            ForEach(store.items.filter { $0.group == g }) { row($0) }
                        }
                    }
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
                Button { Task { await store.scan() } } label: {
                    Label("Rescan", systemImage: "arrow.clockwise")
                }.disabled(store.busy)
                Spacer()
                Text(summary).foregroundStyle(.secondary)
                Button("Clean selected") { store.confirming = true }
                    .buttonStyle(.borderedProminent)
                    .disabled(store.selected.isEmpty || store.busy)
            }
            .padding(12)
        }
        .alert("Delete selected items?", isPresented: $store.confirming) {
            Button("Clean", role: .destructive) { Task { await store.cleanSelected() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This cannot be undone.\n\n" + store.selected.map {
                "• \($0.title) (\(fmt($0.size)))" + ($0.note.isEmpty ? "" : "\n   \($0.note)")
            }.joined(separator: "\n"))
        }
        .alert("Cleaner", isPresented: Binding(get: { store.message != nil },
                                               set: { if !$0 { store.message = nil } })) {
            Button("OK") { store.message = nil }
        } message: { Text(store.message ?? "") }
    }

    private var summary: String {
        let sel = store.selected
        let known = sel.reduce(Int64(0)) { $0 + ($1.size ?? 0) }
        return "\(sel.count) selected · \(fmt(known))" + (sel.contains { $0.size == nil } ? " + more" : "")
    }

    @ViewBuilder private func row(_ item: CleanItem) -> some View {
        HStack(spacing: 12) {
            if item.empty {
                Image(systemName: item.failed ? "xmark.circle.fill" : "checkmark.circle.fill")
                    .foregroundStyle(item.failed ? .red : .green)
            } else {
                Toggle("", isOn: Binding(get: { item.selected },
                                         set: { store.setSelected(item.id, $0) }))
                    .labelsHidden()
            }
            VStack(alignment: .leading, spacing: 2) {
                Text(item.title)
                Text(item.subtitle).font(.caption).foregroundStyle(.secondary).lineLimit(2)
            }
            Spacer()
            if item.empty {
                Text(item.failed ? "Scan failed" : "Clean ✓")
                    .foregroundStyle(item.failed ? .red : .green)
            } else {
                Text(fmt(item.size)).foregroundStyle(.secondary).monospacedDigit()
            }
        }
        .padding(.vertical, 2)
    }
}
