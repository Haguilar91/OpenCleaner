import SwiftUI
import AppKit
import QuickLookThumbnailing
import AVKit
import ImageIO

private let textExt: Set<String> = ["txt", "log", "md", "json", "xml", "csv", "html", "htm", "ini", "conf",
    "cfg", "plist", "yaml", "yml", "swift", "py", "js", "sh", "srt", "vcf", "gpx", "toml"]

/// First few KB of a text file, or nil if it looks binary.
private func readHead(_ url: URL) -> String? {
    guard let h = try? FileHandle(forReadingFrom: url) else { return nil }
    defer { try? h.close() }
    guard let data = try? h.read(upToCount: 4000) else { return nil }
    if data.isEmpty { return "" }
    if data.contains(0) { return nil }
    return String(decoding: data, as: UTF8.self)
}

private let videoExt: Set<String> = ["mp4", "m4v", "mov"]
private let audioExt: Set<String> = ["mp3", "flac", "m4a", "aac", "wav", "aiff", "alac"]

/// Decoded image, scaled down so very large photos stay light.
private func loadImage(_ url: URL) -> NSImage? {
    guard let src = CGImageSourceCreateWithURL(url as CFURL, nil) else { return nil }
    let opts: [CFString: Any] = [
        kCGImageSourceCreateThumbnailFromImageAlways: true,
        kCGImageSourceCreateThumbnailWithTransform: true,
        kCGImageSourceThumbnailMaxPixelSize: 2000,
    ]
    guard let cg = CGImageSourceCreateThumbnailAtIndex(src, 0, opts as CFDictionary) else { return nil }
    return NSImage(cgImage: cg, size: NSSize(width: cg.width, height: cg.height))
}

/// Largest size that fits in maxW x maxH with the image's aspect ratio (small images are scaled up to at least 360 wide).
private func fitSize(_ size: NSSize, maxW: CGFloat, maxH: CGFloat) -> CGSize {
    guard size.width > 0, size.height > 0 else { return CGSize(width: maxW, height: maxH) }
    let k = min(maxW / size.width, maxH / size.height)
    return CGSize(width: size.width * k, height: size.height * k)
}

private func thumbnail(_ url: URL) async -> NSImage? {
    let req = QLThumbnailGenerator.Request(fileAt: url, size: CGSize(width: 480, height: 320),
                                           scale: 2, representationTypes: .thumbnail)
    return await withCheckedContinuation { cont in
        QLThumbnailGenerator.shared.generateBestRepresentation(for: req) { rep, _ in
            cont.resume(returning: rep?.nsImage)
        }
    }
}

@MainActor
final class PreviewModel: ObservableObject {
    @Published var image: NSImage?
    @Published var text: String?
    @Published var loaded = false
    @Published var player: AVPlayer?
    @Published var isAudio = false

    func load(_ url: URL) async {
        let ext = url.pathExtension.lowercased()
        if videoExt.contains(ext) || audioExt.contains(ext) {
            let playable = (try? await AVURLAsset(url: url).load(.isPlayable)) ?? false
            if playable {
                isAudio = audioExt.contains(ext)
                player = AVPlayer(url: url)
                loaded = true
                return
            }
        }
        if textExt.contains(ext) {
            text = await Task.detached { readHead(url) }.value
        }
        if text == nil {
            image = await Task.detached { loadImage(url) }.value
            if image == nil { image = await thumbnail(url) }
        }
        loaded = true
    }

    func stop() { player?.pause(); player = nil }
}

@MainActor
final class ReviewModel: ObservableObject {
    @Published var preview: URL?
}

/// Preview of one file (image / video / PDF thumbnail, or the start of a text file) so the user can
/// decide whether to delete it. `onTrash` adds a Move to Trash button.
struct PreviewSheet: View {
    let url: URL
    var deleteTitle = "Move to Trash"
    var onTrash: ((URL) -> Void)? = nil
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model = PreviewModel()

    private var info: String {
        let v = try? url.resourceValues(forKeys: [.fileSizeKey, .contentModificationDateKey, .localizedTypeDescriptionKey])
        let size = fmt(Int64(v?.fileSize ?? 0))
        let when = v?.contentModificationDate.map { $0.formatted(date: .abbreviated, time: .shortened) } ?? ""
        return "\(size) \u{00B7} \(v?.localizedTypeDescription ?? "File")\n\(when)\n\(url.deletingLastPathComponent().path)"
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Text(url.lastPathComponent).font(.headline).lineLimit(2)
            Group {
                if let player = model.player {
                    PlayerView(player: player)
                        .frame(minWidth: 680, maxWidth: .infinity, minHeight: model.isAudio ? 70 : 420,
                               maxHeight: model.isAudio ? 70 : 420)
                        .clipShape(RoundedRectangle(cornerRadius: 8))
                } else if let image = model.image {
                    let fit = fitSize(image.size, maxW: 680, maxH: 520)
                    Image(nsImage: image).resizable().frame(width: fit.width, height: fit.height)
                } else if let text = model.text {
                    ScrollView {
                        Text(text).font(.system(.caption, design: .monospaced))
                            .frame(maxWidth: .infinity, alignment: .leading).textSelection(.enabled)
                    }
                    .frame(height: 360)
                } else if model.loaded {
                    Text("No preview for this file type. Use Open to view it in another app.")
                        .font(.caption).foregroundStyle(.secondary)
                } else {
                    ProgressView().frame(maxWidth: .infinity)
                }
            }
            .frame(maxWidth: .infinity)
            Text(info).font(.caption).foregroundStyle(.secondary).textSelection(.enabled)
            HStack {
                if let onTrash {
                    Button(role: .destructive) { dismiss(); onTrash(url) } label: {
                        Label(deleteTitle, systemImage: "trash")
                    }
                }
                Spacer()
                Button("Reveal") { NSWorkspace.shared.activateFileViewerSelecting([url]) }
                Button("Open") { NSWorkspace.shared.open(url) }
                Button("Close") { dismiss() }.keyboardShortcut(.cancelAction)
            }
        }
        .padding(20)
        .frame(width: 720)
        .task { await model.load(url) }
        .onDisappear { model.stop() }
    }
}

/// Lists what a Clean item would delete so each file can be previewed before ticking it.
struct ReviewSheet: View {
    let item: CleanItem
    @Environment(\.dismiss) private var dismiss
    @StateObject private var model = ReviewModel()

    private var shown: [URL] { Array(item.paths.prefix(200)) }

    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            Text(item.title).font(.headline)
            Text("Click a file to preview it.").font(.caption).foregroundStyle(.secondary)
            ScrollView {
                LazyVStack(spacing: 0) {
                    ForEach(shown, id: \.self) { u in
                        let isDir = FS.isRealDir(u)
                        Button { if !isDir { model.preview = u } } label: {
                            HStack(spacing: 8) {
                                Image(systemName: isDir ? "folder.fill" : "doc").frame(width: 18)
                                Text(u.lastPathComponent).lineLimit(1)
                                Spacer()
                                Text(isDir ? "folder" : fmt(Int64((try? u.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0)))
                                    .font(.caption).foregroundStyle(.secondary)
                            }
                            .padding(.vertical, 5).contentShape(Rectangle())
                        }
                        .buttonStyle(.plain)
                        Divider()
                    }
                    if item.paths.count > shown.count {
                        Text("…and \(item.paths.count - shown.count) more")
                            .font(.caption).padding(.top, 6)
                    }
                }
            }
            .frame(height: 300)
            HStack { Spacer(); Button("Close") { dismiss() }.keyboardShortcut(.cancelAction) }
        }
        .padding(20)
        .frame(width: 480)
        .sheet(item: Binding(get: { model.preview.map(Identified.init) }, set: { model.preview = $0?.url })) {
            PreviewSheet(url: $0.url)
        }
    }
}

struct Identified: Identifiable { let url: URL; var id: URL { url } }

/// AppKit's AVPlayerView (play/pause, seek bar, volume). SwiftUI's own VideoPlayer crashes on some macOS builds.
struct PlayerView: NSViewRepresentable {
    let player: AVPlayer

    func makeNSView(context: Context) -> AVPlayerView {
        let v = AVPlayerView()
        v.controlsStyle = .floating
        v.player = player
        return v
    }

    func updateNSView(_ v: AVPlayerView, context: Context) {
        if v.player !== player { v.player = player }
    }
}
