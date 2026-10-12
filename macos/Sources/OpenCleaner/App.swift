import SwiftUI

@main
struct OpenCleanerApp: App {
    @StateObject private var store = CleanerStore()

    var body: some Scene {
        WindowGroup("OpenCleaner") {
            RootView()
                .environmentObject(store)
                .tint(Color(red: 0.145, green: 0.388, blue: 0.922))   // Trust Blue #2563EB
                .frame(minWidth: 760, minHeight: 640)
                .task { await store.scan() }
                .task { await store.checkForUpdates(manual: false) }
        }
        .windowStyle(.hiddenTitleBar)
        .defaultSize(width: 840, height: 760)
    }
}

/// Header bar + the two pages, laid out like the Linux (GTK/libadwaita) version.
struct RootView: View {
    @EnvironmentObject var store: CleanerStore
    @StateObject private var explorer = ExplorerModel()
    @StateObject private var appsModel = AppsModel()

    var body: some View {
        VStack(spacing: 0) {
            header
            Divider()
            if store.tab == 0 {
                CleanView()
            } else if store.tab == 1 {
                ExplorerView(model: explorer)
            } else {
                AppsView(model: appsModel)
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
                Text("Apps").tag(2)
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(width: 300)
            Spacer()
            if let u = store.update {
                Button(store.updating ? "Updating\u{2026}" : "Update to \(u.version)") { store.offerUpdate = true }
                    .buttonStyle(.borderedProminent).controlSize(.small).disabled(store.updating)
            } else {
                Button { Task { await store.checkForUpdates(manual: true) } } label: {
                    Image(systemName: "arrow.down.circle")
                }
                .buttonStyle(.borderless).help("Check for updates (OpenCleaner \(Updater.currentVersion))")
            }
            Text(store.freeText).font(.callout).foregroundStyle(.secondary)
        }
        .alert("Update to OpenCleaner \(store.update?.version ?? "")?", isPresented: $store.offerUpdate) {
            Button("Update and restart") { Task { await store.installUpdate() } }
            Button("Release page") { if let u = store.update?.page { NSWorkspace.shared.open(u) } }
            Button("Later", role: .cancel) {}
        } message: {
            Text("You have \(Updater.currentVersion). The update is downloaded from GitHub, checked against the release checksums, and installed in place of this app.\n\n" + String((store.update?.notes ?? "").prefix(600)))
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
        } else if store.tab == 2 {
            appsModel.load()
        } else {
            Task { await store.scan() }
        }
    }
}
