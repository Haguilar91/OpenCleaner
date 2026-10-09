// swift-tools-version:5.9
import PackageDescription

let package = Package(
    name: "OpenCleaner",
    platforms: [.macOS(.v13)],
    targets: [
        .executableTarget(name: "OpenCleaner", path: "Sources/OpenCleaner")
    ]
)
