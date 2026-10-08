import SwiftUI

@main
struct MacCleanerApp: App {
    @StateObject private var store = CleanerStore()

    var body: some Scene {
        WindowGroup("Mac Cleaner") {
            TabView {
                CleanView()
                    .tabItem { Label("Clean", systemImage: "sparkles") }
                ExplorerView()
                    .tabItem { Label("Disk Usage", systemImage: "internaldrive") }
            }
            .environmentObject(store)
            .frame(minWidth: 720, minHeight: 640)
            .toolbar {
                ToolbarItem(placement: .primaryAction) {
                    Text(store.freeText).foregroundStyle(.secondary).font(.callout)
                }
            }
            .task { await store.scan() }
        }
        .defaultSize(width: 760, height: 720)
    }
}
