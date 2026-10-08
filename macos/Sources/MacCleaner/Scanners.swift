import Foundation

struct CleanItem: Identifiable, Sendable {
    let id = UUID()
    var group: String
    var title: String
    var subtitle: String
    var size: Int64?
    var selected = false
    var paths: [URL] = []          // user-owned paths to delete
    var command: [String]? = nil   // run as the user, absolute executable first
    var note = ""
    var empty = false              // checked, nothing to clean
    var failed = false
}

enum Scanners {
    static let cacheMin: Int64 = 100 * 1024 * 1024
    static var lib: URL { URL(fileURLWithPath: home).appendingPathComponent("Library") }

    // (scanner, group, label shown when nothing is found)
    static let all: [(() -> [CleanItem], String, String)] = [
        (homebrewCache, "Homebrew", "Download cache and old versions"),
        (homebrewOrphans, "Homebrew", "Unneeded dependencies"),
        (logs, "System junk", "User logs and crash reports"),
        (userCaches, "System junk", "Large app caches"),
        (trash, "System junk", "Trash"),
        (xcodeData, "Developer", "Xcode build data"),
        (simulators, "Developer", "Unavailable simulators"),
        (iosBackups, "Device backups", "iPhone and iPad backups"),
        (steam, "Steam", "Steam leftovers"),
    ]

    static func runAll() -> [CleanItem] {
        var out: [CleanItem] = []
        for (fn, group, label) in all {
            let found = fn()
            out += found.isEmpty
                ? [CleanItem(group: group, title: label, subtitle: "Nothing to clean", size: 0, empty: true)]
                : found
        }
        return out
    }

    static func clean(_ item: CleanItem) -> String? {
        for p in item.paths {
            do { try FS.safeRemove(p) } catch { return error.localizedDescription }
        }
        if let cmd = item.command {
            let r = Shell.run(cmd)
            if r.status != 0 { return r.out.trimmingCharacters(in: .whitespacesAndNewlines) }
        }
        return nil
    }

    // MARK: Homebrew
    static func parseSize(_ text: String) -> Int64? {
        guard let m = text.range(of: #"([\d.]+)\s*([KMGT]?B)"#, options: .regularExpression) else { return nil }
        let s = String(text[m])
        let num = Double(s.prefix { $0.isNumber || $0 == "." }) ?? 0
        let unit = s.drop { $0.isNumber || $0 == "." || $0 == " " }.uppercased()
        let mult: [String: Double] = ["B": 1, "KB": 1e3, "MB": 1e6, "GB": 1e9, "TB": 1e12]
        return Int64(num * (mult[unit] ?? 1))
    }

    static func homebrewCache() -> [CleanItem] {
        guard let brew = Shell.brew else { return [] }
        let out = Shell.run([brew, "cleanup", "-n", "-s"]).out
        guard let line = out.split(separator: "\n").first(where: { $0.contains("free approximately") })
        else { return [] }
        return [CleanItem(group: "Homebrew", title: "Download cache and old versions",
                          subtitle: "Runs brew cleanup -s", size: parseSize(String(line)),
                          selected: true, command: [brew, "cleanup", "-s"])]
    }

    static func homebrewOrphans() -> [CleanItem] {
        guard let brew = Shell.brew else { return [] }
        let lines = Shell.run([brew, "autoremove", "--dry-run"]).out.split(separator: "\n").map(String.init)
        guard let i = lines.firstIndex(where: { $0.contains("autoremove") && $0.hasPrefix("==>") }) else { return [] }
        let names = lines[(i + 1)...].filter { !$0.isEmpty && !$0.hasPrefix("==>") }
        if names.isEmpty { return [] }
        return [CleanItem(group: "Homebrew", title: "Unneeded dependencies (\(names.count))",
                          subtitle: names.prefix(8).joined(separator: ", "), size: nil,
                          command: [brew, "autoremove"], note: "Removes: " + names.joined(separator: ", "))]
    }

    // MARK: system junk
    static func logs() -> [CleanItem] {
        let dir = lib.appendingPathComponent("Logs")
        let kids = FS.children(dir)
        let size = kids.reduce(Int64(0)) { $0 + FS.size($1) }
        if size < 10 * 1024 * 1024 { return [] }
        return [CleanItem(group: "System junk", title: "User logs and crash reports",
                          subtitle: "~/Library/Logs", size: size, selected: true, paths: kids)]
    }

    static func userCaches() -> [CleanItem] {
        let dir = lib.appendingPathComponent("Caches")
        return FS.children(dir).compactMap { url in
            let name = url.lastPathComponent
            guard FS.isRealDir(url), !name.hasPrefix("com.apple.") else { return nil }
            let size = FS.size(url)
            guard size >= cacheMin else { return nil }
            return CleanItem(group: "System junk", title: name,
                             subtitle: "App cache, rebuilt automatically", size: size, paths: [url])
        }.sorted { ($0.size ?? 0) > ($1.size ?? 0) }
    }

    static func trash() -> [CleanItem] {
        let dir = URL(fileURLWithPath: home).appendingPathComponent(".Trash")
        let kids = FS.children(dir)
        let size = kids.reduce(Int64(0)) { $0 + FS.size($1) }
        if kids.isEmpty || size < 1024 * 1024 { return [] }
        return [CleanItem(group: "System junk", title: "Trash",
                          subtitle: "Permanently deletes everything in your Trash", size: size, paths: kids)]
    }

    // MARK: developer
    static func xcodeData() -> [CleanItem] {
        let dev = lib.appendingPathComponent("Developer")
        let spots: [(String, String, Bool)] = [
            ("Xcode/DerivedData", "Build products, rebuilt on next build", true),
            ("Xcode/iOS DeviceSupport", "Debug symbols for devices you plugged in", false),
            ("Xcode/Archives", "Archived app builds. Keep any you still need", false),
        ]
        return spots.compactMap { path, why, on in
            let url = dev.appendingPathComponent(path)
            guard FileManager.default.fileExists(atPath: url.path) else { return nil }
            let kids = FS.children(url)
            let size = kids.reduce(Int64(0)) { $0 + FS.size($1) }
            guard size > 50 * 1024 * 1024 else { return nil }
            return CleanItem(group: "Developer", title: "Xcode " + (path as NSString).lastPathComponent,
                             subtitle: why, size: size, selected: on, paths: kids)
        }
    }

    static func simulators() -> [CleanItem] {
        guard FileManager.default.isExecutableFile(atPath: "/usr/bin/xcrun"),
              FileManager.default.fileExists(atPath: lib.appendingPathComponent("Developer/CoreSimulator").path)
        else { return [] }
        let r = Shell.run(["/usr/bin/xcrun", "simctl", "list", "devices", "unavailable"])
        guard r.status == 0, r.out.contains("(") , r.out.contains("unavailable") else { return [] }
        let count = r.out.split(separator: "\n").filter { $0.contains("(") && $0.contains("-") && !$0.hasPrefix("==") }.count
        if count == 0 { return [] }
        return [CleanItem(group: "Developer", title: "Unavailable simulators (\(count))",
                          subtitle: "Simulators for removed iOS runtimes", size: nil, selected: true,
                          command: ["/usr/bin/xcrun", "simctl", "delete", "unavailable"])]
    }


    // MARK: Steam
    private final class Box: @unchecked Sendable { var data: Data? }

    static var steamRoot: URL { lib.appendingPathComponent("Application Support/Steam") }

    /// Game name from the Steam store, cached on disk. Nil when offline.
    static func steamName(_ id: String) -> String? {
        let cacheURL = lib.appendingPathComponent("Application Support/MacCleaner/steam_names.json")
        var cache = (try? JSONDecoder().decode([String: String].self, from: Data(contentsOf: cacheURL))) ?? [:]
        if let v = cache[id] { return v }
        guard let url = URL(string: "https://store.steampowered.com/api/appdetails?appids=\(id)&filters=basic")
        else { return nil }
        var req = URLRequest(url: url)
        req.timeoutInterval = 4
        let sem = DispatchSemaphore(value: 0)
        let box = Box()
        URLSession.shared.dataTask(with: req) { d, _, _ in box.data = d; sem.signal() }.resume()
        _ = sem.wait(timeout: .now() + 6)
        guard let data = box.data,
              let json = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        var name = "App \(id) (not in Steam store)"
        if let entry = json[id] as? [String: Any], entry["success"] as? Bool == true,
           let d = entry["data"] as? [String: Any], let n = d["name"] as? String { name = n }
        cache[id] = name
        try? FileManager.default.createDirectory(at: cacheURL.deletingLastPathComponent(),
                                                 withIntermediateDirectories: true)
        try? JSONEncoder().encode(cache).write(to: cacheURL)
        return name
    }

    static func steam() -> [CleanItem] {
        let fm = FileManager.default
        let root = steamRoot
        guard fm.fileExists(atPath: root.path) else { return [] }

        // all Steam library folders, from libraryfolders.vdf
        var libs = [root]
        var missing = false
        if let vdf = try? String(contentsOf: root.appendingPathComponent("steamapps/libraryfolders.vdf"),
                                 encoding: .utf8),
           let re = try? NSRegularExpression(pattern: "\"path\"\\s+\"([^\"]+)\"") {
            let ns = vdf as NSString
            for m in re.matches(in: vdf, range: NSRange(location: 0, length: ns.length)) {
                let p = ns.substring(with: m.range(at: 1))
                if URL(fileURLWithPath: p).standardizedFileURL.path == root.standardizedFileURL.path { continue }
                if fm.fileExists(atPath: p) { libs.append(URL(fileURLWithPath: p)) } else { missing = true }
            }
        }

        var installed = Set<String>()
        for l in libs {
            for f in FS.children(l.appendingPathComponent("steamapps"))
            where f.lastPathComponent.hasPrefix("appmanifest_") && f.pathExtension == "acf" {
                installed.insert(f.deletingPathExtension().lastPathComponent
                    .replacingOccurrences(of: "appmanifest_", with: ""))
            }
        }
        let warn = missing ? " Some Steam library drives are not connected, so the game may live there." : ""
        let running = Shell.run(["/usr/bin/pgrep", "-x", "steam_osx"]).status == 0
        // deletes are limited to the home folder, so libraries on other drives are skipped
        func inHome(_ u: URL) -> Bool { u.standardizedFileURL.path.hasPrefix(home + "/") }

        var items: [CleanItem] = []

        // shader caches of games that are no longer installed
        for l in libs where inHome(l) {
            for dir in FS.children(l.appendingPathComponent("steamapps/shadercache")) {
                let id = dir.lastPathComponent
                guard id.allSatisfy(\.isNumber), !installed.contains(id) else { continue }
                let size = FS.size(dir)
                guard size >= 1024 * 1024 else { continue }
                let name = steamName(id)
                items.append(CleanItem(group: "Steam", title: "Shader cache: " + (name ?? "app \(id)"),
                                       subtitle: "Game not installed (app \(id)). Steam rebuilds this if you reinstall." + warn,
                                       size: size, selected: true, paths: [dir]))
            }
        }

        let html = root.appendingPathComponent("config/htmlcache")
        let htmlKids = FS.children(html)
        let htmlSize = htmlKids.reduce(Int64(0)) { $0 + FS.size($1) }
        if htmlSize >= cacheMin {
            items.append(CleanItem(group: "Steam", title: "Steam browser cache",
                                   subtitle: running ? "Steam is running. Close it first, or in-use files are skipped."
                                                     : "Cache of the Steam store and community pages",
                                   size: htmlSize, paths: htmlKids))
        }

        var partial: [URL] = []
        for l in libs where inHome(l) {
            for sub in ["downloading", "temp"] {
                partial += FS.children(l.appendingPathComponent("steamapps/\(sub)"))
            }
        }
        let partialSize = partial.reduce(Int64(0)) { $0 + FS.size($1) }
        if partialSize >= 10 * 1024 * 1024 {
            items.append(CleanItem(group: "Steam", title: "Unfinished game downloads",
                                   subtitle: "Partial downloads. Don't clean while a download you want to resume is paused.",
                                   size: partialSize, paths: partial))
        }
        return items
    }

    // MARK: device backups
    static func iosBackups() -> [CleanItem] {
        let dir = lib.appendingPathComponent("Application Support/MobileSync/Backup")
        return FS.children(dir).filter { FS.isRealDir($0) }.map { url in
            let info = (try? Data(contentsOf: url.appendingPathComponent("Info.plist")))
                .flatMap { try? PropertyListSerialization.propertyList(from: $0, format: nil) as? [String: Any] }
            let name = info?["Device Name"] as? String ?? url.lastPathComponent
            let date = (info?["Last Backup Date"] as? Date)
                .map { DateFormatter.localizedString(from: $0, dateStyle: .medium, timeStyle: .none) } ?? "unknown date"
            return CleanItem(group: "Device backups", title: name,
                             subtitle: "Last backup \(date). Cannot be recovered once deleted.",
                             size: FS.size(url), paths: [url])
        }
    }
}
