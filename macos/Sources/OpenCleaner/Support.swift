import Foundation

let home = NSHomeDirectory()

func fmt(_ bytes: Int64?) -> String {
    guard let bytes else { return tr("size unknown") }
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
    /// Allocated size of a file or folder tree. Symlinks count as zero. With `sameVolume`, folders on
    /// another volume (external or network drives under /Volumes, /System/Volumes/Data) are not entered,
    /// so "System (/)" neither double counts nor walks other drives. Stops early when the task is cancelled.
    static func size(_ url: URL, sameVolume: Bool = false) -> Int64 {
        let keys: Set<URLResourceKey> = [.totalFileAllocatedSizeKey, .fileAllocatedSizeKey,
                                         .isDirectoryKey, .isSymbolicLinkKey, .volumeIdentifierKey]
        let volume = sameVolume ? (try? url.resourceValues(forKeys: [.volumeIdentifierKey]))?.volumeIdentifier : nil
        guard let v = try? url.resourceValues(forKeys: keys) else { return 0 }
        if v.isSymbolicLink == true { return 0 }
        if v.isDirectory != true {
            return Int64(v.totalFileAllocatedSize ?? v.fileAllocatedSize ?? 0)
        }
        var total: Int64 = 0
        let e = FileManager.default.enumerator(at: url, includingPropertiesForKeys: Array(keys),
                                               options: [], errorHandler: { _, _ in true })
        var count = 0
        while let f = e?.nextObject() as? URL {
            count += 1
            if count % 2000 == 0 && Task.isCancelled { break }
            guard let rv = try? f.resourceValues(forKeys: keys), rv.isSymbolicLink != true else { continue }
            if rv.isDirectory == true {
                if let volume, let vol = rv.volumeIdentifier, !vol.isEqual(volume) { e?.skipDescendants() }
                continue
            }
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

    /// Like `children`, but a folder macOS will not let us read throws `ScanError.needsFullDiskAccess`
    /// instead of looking empty. A missing folder is just empty.
    static func childrenOrThrow(_ url: URL) throws -> [URL] {
        do {
            return try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil)
        } catch {
            let code = (error as NSError).code
            if code == NSFileReadNoSuchFileError { return [] }
            if code == NSFileReadNoPermissionError { throw ScanError.needsFullDiskAccess }
            throw error
        }
    }

    /// Deletes a file, link or folder tree. What cannot be deleted (in use, protected) is skipped,
    /// never forced; returns how many entries were skipped.
    static func removeTree(_ url: URL) -> Int {
        let fm = FileManager.default
        if (try? fm.removeItem(at: url)) != nil { return 0 }      // a link is removed itself, not its target
        guard isRealDir(url) else { return fm.fileExists(atPath: url.path) ? 1 : 0 }
        var skipped = 0
        for c in children(url) { skipped += removeTree(c) }
        if (try? fm.removeItem(at: url)) == nil && skipped == 0 { skipped = 1 }
        return skipped
    }

    /// Delete only inside the home folder, never the home folder itself. The parent folder is checked with
    /// symlinks resolved, so a link inside ~/Library cannot lead outside home. Returns how many entries
    /// were skipped because they were in use or protected.
    @discardableResult
    static func safeRemove(_ url: URL) throws -> Int {
        let realHome = URL(fileURLWithPath: home).resolvingSymlinksInPath().path
        let parent = url.standardizedFileURL.deletingLastPathComponent().resolvingSymlinksInPath().path
        let p = url.standardizedFileURL.path
        guard p.hasPrefix(home + "/"), (parent + "/").hasPrefix(realHome + "/") else {
            throw NSError(domain: "OpenCleaner", code: 1,
                          userInfo: [NSLocalizedDescriptionKey: tr("Refusing to delete outside home: {0}", p)])
        }
        return removeTree(url)
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
