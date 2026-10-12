import Foundation
import AppKit
import CryptoKit

/// A GitHub release of OpenCleaner.
struct Release {
    let version: String          // "1.3.0", from the tag "v1.3.0"
    let notes: String
    let page: URL
    let assets: [String: URL]    // file name -> download URL
}

/// Updates from the GitHub releases of Haguilar91/OpenCleaner: check, download the macOS zip,
/// verify it against the release's SHA256SUMS.txt, replace this app and relaunch.
enum Updater {
    static let api = URL(string: "https://api.github.com/repos/Haguilar91/OpenCleaner/releases/latest")!

    static var currentVersion: String {
        Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0"
    }

    /// "v1.3.0" / "1.3" -> [1, 3, 0]
    static func parts(_ v: String) -> [Int] {
        v.trimmingCharacters(in: CharacterSet(charactersIn: "vV ")).split(separator: ".")
            .map { Int($0.prefix { $0.isNumber }) ?? 0 }
    }

    static func isNewer(_ a: String, than b: String) -> Bool {
        let x = parts(a), y = parts(b)
        for i in 0..<max(x.count, y.count) {
            let p = i < x.count ? x[i] : 0, q = i < y.count ? y[i] : 0
            if p != q { return p > q }
        }
        return false
    }

    static func latest() async throws -> Release {
        var req = URLRequest(url: api, timeoutInterval: 20)
        req.setValue("application/vnd.github+json", forHTTPHeaderField: "Accept")
        let (data, response) = try await URLSession.shared.data(for: req)
        guard (response as? HTTPURLResponse)?.statusCode == 200,
              let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
              let tag = json["tag_name"] as? String,
              let page = (json["html_url"] as? String).flatMap(URL.init(string:)) else {
            throw NSError(domain: "OpenCleaner", code: 1, userInfo: [NSLocalizedDescriptionKey: "No release found on GitHub."])
        }
        var assets: [String: URL] = [:]
        for a in json["assets"] as? [[String: Any]] ?? [] {
            if let n = a["name"] as? String, let u = (a["browser_download_url"] as? String).flatMap(URL.init(string:)) {
                assets[n] = u
            }
        }
        return Release(version: tag.trimmingCharacters(in: CharacterSet(charactersIn: "vV")),
                       notes: json["body"] as? String ?? "", page: page, assets: assets)
    }

    /// Downloads, verifies and stages the new app, then quits; a small script swaps the app and relaunches it.
    static func install(_ r: Release) async throws {
        func fail(_ m: String) -> NSError { NSError(domain: "OpenCleaner", code: 2, userInfo: [NSLocalizedDescriptionKey: m]) }
        guard let (name, url) = r.assets.first(where: { $0.key.hasSuffix("-macOS.zip") }) else {
            throw fail("This release has no macOS download.")
        }
        guard let sumsURL = r.assets["SHA256SUMS.txt"] else { throw fail("This release has no checksums, so it was not installed.") }
        let (zip, _) = try await URLSession.shared.download(from: url)
        let (sumsFile, _) = try await URLSession.shared.download(from: sumsURL)
        let sums = try String(contentsOf: sumsFile, encoding: .utf8)
        let digest = SHA256.hash(data: try Data(contentsOf: zip)).map { String(format: "%02x", $0) }.joined()
        guard sums.split(separator: "\n").contains(where: { $0.hasPrefix(digest) && $0.hasSuffix(name) }) else {
            throw fail("The download did not match the release checksum, so it was not installed.")
        }
        let stage = FileManager.default.temporaryDirectory.appendingPathComponent("opencleaner-update-\(r.version)")
        try? FileManager.default.removeItem(at: stage)
        try FileManager.default.createDirectory(at: stage, withIntermediateDirectories: true)
        let unzip = Shell.run(["/usr/bin/ditto", "-x", "-k", zip.path, stage.path])
        let newApp = stage.appendingPathComponent("OpenCleaner.app")
        guard unzip.status == 0, FileManager.default.fileExists(atPath: newApp.path) else { throw fail("Could not unpack the update.") }
        let target = Bundle.main.bundleURL
        guard FileManager.default.isWritableFile(atPath: target.deletingLastPathComponent().path) else {
            throw fail("OpenCleaner cannot replace itself in \(target.deletingLastPathComponent().path). Download it from the release page instead.")
        }
        let q = { (s: String) in "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'" }
        let script = """
        #!/bin/sh
        while kill -0 \(ProcessInfo.processInfo.processIdentifier) 2>/dev/null; do sleep 0.5; done
        rm -rf \(q(target.path + ".old"))
        mv \(q(target.path)) \(q(target.path + ".old")) && mv \(q(newApp.path)) \(q(target.path)) && rm -rf \(q(target.path + ".old"))
        open \(q(target.path))
        """
        let scriptURL = stage.appendingPathComponent("swap.sh")
        try script.write(to: scriptURL, atomically: true, encoding: .utf8)
        let p = Process()
        p.executableURL = URL(fileURLWithPath: "/bin/sh")
        p.arguments = [scriptURL.path]
        try p.run()                                   // keeps running after OpenCleaner quits
        await MainActor.run { NSApp.terminate(nil) }
    }
}
