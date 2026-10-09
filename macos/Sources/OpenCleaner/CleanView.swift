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
                Text(summary).foregroundStyle(.secondary)
                Spacer()
                Button("Clean selected") { store.confirming = true }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(store.selected.isEmpty || store.busy)
            }
            .padding(.horizontal, 24)
            .padding(.vertical, 12)
            .background(.bar)
        }
        .alert("Delete selected items?", isPresented: $store.confirming) {
            Button("Clean", role: .destructive) { Task { await store.cleanSelected() } }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("This cannot be undone.\n\n" + store.selected.map {
                "\u{2022} \($0.title) (\(fmt($0.size)))" + ($0.note.isEmpty ? "" : "\n   \($0.note)")
            }.joined(separator: "\n"))
        }
        .alert("OpenCleaner", isPresented: Binding(get: { store.message != nil },
                                                   set: { if !$0 { store.message = nil } })) {
            Button("OK") { store.message = nil }
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
        return "\(sel.count) selected \u{00B7} \(fmt(known))" + (sel.contains { $0.size == nil } ? " + more" : "")
    }

    @ViewBuilder private func row(_ item: CleanItem) -> some View {
        HStack(spacing: 12) {
            if item.empty {
                Image(systemName: item.failed ? "xmark.circle.fill" : "checkmark.circle.fill")
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
                Text(item.failed ? "Scan failed" : "Clean \u{2713}")
                    .foregroundStyle(item.failed ? Color.red : Color.green)
            } else {
                Text(fmt(item.size)).foregroundStyle(.secondary).monospacedDigit()
            }
        }
        .padding(.horizontal, 14)
        .padding(.vertical, 10)
    }
}
