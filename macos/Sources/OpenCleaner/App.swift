import SwiftUI

@main
struct OpenCleanerApp: App {
    @StateObject private var store = CleanerStore()

    var body: some Scene {
        WindowGroup("OpenCleaner") {
            RootView()
                .environmentObject(store)
                .frame(minWidth: 760, minHeight: 640)
                .task { await store.scan() }
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 840, height: 760)
    }
}

/// Header bar + the two pages, laid out like the Linux (GTK/libadwaita) version.
struct RootView: View {
    @EnvironmentObject var store: CleanerStore
    @StateObject private var explorer = ExplorerModel()

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            if store.tab == 0 {
                CleanView()
            } else {
                ExplorerView(model: explorer)
            }
        }
    }

    private var header: some View {
        HStack(spacing: 10) {
            Text("OpenCleaner").font(.system(size: 15, weight: .bold))
            Button { refresh() } label: { Image(systemName: "arrow.clockwise") }
                .buttonStyle(.borderless)
                .help("Rescan")
                .disabled(store.busy)
            Spacer()
            Picker("", selection: $store.tab) {
                Text("Clean").tag(0)
                Text("Disk usage").tag(1)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(width: 230)
            Spacer()
            Text(store.freeText).font(.callout).foregroundStyle(.secondary)
        }
        .padding(.leading, 82)        // room for the window's close/minimise/zoom buttons
        .padding(.trailing, 18)
        .padding(.vertical, 12)
        .background(.bar)
    }

    private func refresh() {
        if store.tab == 1 {
            explorer.reload()
            store.updateFree()
        } else {
            Task { await store.scan() }
        }
    }
}
