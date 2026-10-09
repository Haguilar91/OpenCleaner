import Foundation

let home = NSHomeDirectory()

func fmt(_ bytes: Int64?) -> String {
    guard let bytes else { return "size unknown" }
    return ByteCountFormatter.string(fromByteCount: bytes, countStyle: .file)
}

enum Shell {
    /// Run an executable (absolute path first) and capture stdout+stderr.
    static func run(_ args: [String]) -> (status: Int32, out: String) {
        let p = Process()
        p.executableURL = URL(fileURLWithPath: args[0])
        p.arguments = Array(args.dropFirst())
        p.environment = [
            "PATH": "/opt/homebrew/bin:/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin",
            "HOME": home,
            "HOMEBREW_NO_AUTO_UPDATE": "1",
            "HOMEBREW_NO_ANALYTICS": "1",
        ]
        let pipe = Pipe()
        p.standardOutput = pipe
        p.standardError = pipe
        do { try p.run() } catch { return (-1, "\(error)") }
        let data = pipe.fileHandleForReading.readDataToEndOfFile()
        p.waitUntilExit()
        return (p.terminationStatus, String(decoding: data, as: UTF8.self))
    }

    static var brew: String? {
        ["/opt/homebrew/bin/brew", "/usr/local/bin/brew"]
            .first { FileManager.default.isExecutableFile(atPath: $0) }
    }
}

enum FS {
    /// Allocated size of a file or folder tree. Symlinks count as zero.
    static func size(_ url: URL) -> Int64 {
        let keys: Set<URLResourceKey> = [.totalFileAllocatedSizeKey, .fileAllocatedSizeKey,
                                         .isDirectoryKey, .isSymbolicLinkKey]
        guard let v = try? url.resourceValues(forKeys: keys) else { return 0 }
        if v.isSymbolicLink == true { return 0 }
        if v.isDirectory != true {
            return Int64(v.totalFileAllocatedSize ?? v.fileAllocatedSize ?? 0)
        }
        var total: Int64 = 0
        let e = FileManager.default.enumerator(at: url, includingPropertiesForKeys: Array(keys),
                                               options: [], errorHandler: { _, _ in true })
        while let f = e?.nextObject() as? URL {
            guard let rv = try? f.resourceValues(forKeys: keys),
                  rv.isSymbolicLink != true, rv.isDirectory != true else { continue }
            total += Int64(rv.totalFileAllocatedSize ?? rv.fileAllocatedSize ?? 0)
        }
        return total
    }

    static func isRealDir(_ url: URL) -> Bool {
        let v = try? url.resourceValues(forKeys: [.isDirectoryKey, .isSymbolicLinkKey])
        return v?.isDirectory == true && v?.isSymbolicLink != true
    }

    static func children(_ url: URL) -> [URL] {
        (try? FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil)) ?? []
    }

    /// Delete only inside the home folder, never the home folder itself.
    static func safeRemove(_ url: URL) throws {
        let p = url.standardizedFileURL.path
        guard p.hasPrefix(home + "/"), p != home else {
            throw NSError(domain: "OpenCleaner", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: "Refusing to delete outside home: \(p)"])
        }
        try FileManager.default.removeItem(at: url)   // removes a symlink itself, not its target
    }

    static func freeSpace() -> (free: Int64, total: Int64)? {
        let url = URL(fileURLWithPath: "/")
        guard let v = try? url.resourceValues(forKeys: [.volumeAvailableCapacityForImportantUsageKey,
                                                         .volumeTotalCapacityKey]),
              let free = v.volumeAvailableCapacityForImportantUsage,
              let total = v.volumeTotalCapacity else { return nil }
        return (free, Int64(total))
    }
}
