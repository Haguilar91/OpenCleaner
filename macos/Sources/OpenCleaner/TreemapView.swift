import SwiftUI

/// Squarified treemap layout (Bruls, Huizing, van Wijk): blocks stay close to square so they are easy to read.
enum Treemap {
    /// `sizes` must be positive and sorted largest first. Returns one rect per size, in order.
    static func layout(_ sizes: [Double], in bounds: CGRect) -> [CGRect] {
        let total = sizes.reduce(0, +)
        guard total > 0, bounds.width > 0, bounds.height > 0 else { return [] }
        let scale = Double(bounds.width * bounds.height) / total
        let areas = sizes.map { $0 * scale }
        var rects = [CGRect](repeating: .zero, count: areas.count)
        var rect = bounds
        var i = 0
        while i < areas.count {
            let side = Double(min(rect.width, rect.height))
            var row = [areas[i]]
            var j = i + 1
            while j < areas.count {
                let next = row + [areas[j]]
                if worst(next, side) <= worst(row, side) { row = next; j += 1 } else { break }
            }
            let sum = row.reduce(0, +)
            if rect.width >= rect.height {          // column on the left
                let w = CGFloat(sum / Double(rect.height))
                var y = rect.minY
                for (k, a) in row.enumerated() {
                    let h = CGFloat(a / Double(w))
                    rects[i + k] = CGRect(x: rect.minX, y: y, width: w, height: h)
                    y += h
                }
                rect = CGRect(x: rect.minX + w, y: rect.minY, width: max(rect.width - w, 0), height: rect.height)
            } else {                                // row on the top
                let h = CGFloat(sum / Double(rect.width))
                var x = rect.minX
                for (k, a) in row.enumerated() {
                    let w = CGFloat(a / Double(h))
                    rects[i + k] = CGRect(x: x, y: rect.minY, width: w, height: h)
                    x += w
                }
                rect = CGRect(x: rect.minX, y: rect.minY + h, width: rect.width, height: max(rect.height - h, 0))
            }
            i = j
        }
        return rects
    }

    private static func worst(_ row: [Double], _ side: Double) -> Double {
        let s = row.reduce(0, +)
        guard let mx = row.max(), let mn = row.min(), s > 0, mn > 0 else { return .infinity }
        return max(side * side * mx / (s * s), s * s / (side * side * mn))
    }
}

/// File categories shown by color, like WinDirStat.
enum Kind: CaseIterable {
    case folder, image, video, audio, document, archive, other

    var order: Int { Kind.allCases.firstIndex(of: self) ?? 0 }

    var label: String {
        switch self {
        case .folder: tr("Folders")
        case .image: tr("Images")
        case .video: tr("Video")
        case .audio: tr("Audio")
        case .document: tr("Documents")
        case .archive: tr("Archives")
        case .other: tr("Other")
        }
    }

    var color: Color {
        switch self {
        case .folder: Color(red: 0.145, green: 0.388, blue: 0.922)   // brand blue
        case .image: Color(red: 0.18, green: 0.62, blue: 0.36)
        case .video: Color(red: 0.55, green: 0.33, blue: 0.80)
        case .audio: Color(red: 0.88, green: 0.52, blue: 0.14)
        case .document: Color(red: 0.10, green: 0.60, blue: 0.62)
        case .archive: Color(red: 0.78, green: 0.27, blue: 0.27)
        case .other: Color(red: 0.42, green: 0.46, blue: 0.54)
        }
    }

    static func of(_ e: Entry) -> Kind {
        if e.isDir { return .folder }
        switch e.id.pathExtension.lowercased() {
        case "jpg", "jpeg", "png", "gif", "heic", "webp", "tiff", "bmp", "raw", "dng", "psd": return .image
        case "mp4", "mov", "m4v", "mkv", "avi", "webm", "wmv": return .video
        case "mp3", "flac", "m4a", "aac", "wav", "aiff", "ogg", "opus": return .audio
        case "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "pages", "numbers", "key", "rtf", "csv": return .document
        case "zip", "dmg", "pkg", "iso", "tar", "gz", "7z", "rar", "xz", "bz2": return .archive
        default: return .other
        }
    }
}

/// Treemap of the current folder: each item is a block sized by its space. Click a folder to open it,
/// click a file to preview it.
struct MapView: View {
    @ObservedObject var model: ExplorerModel

    private var items: [Entry] {
        model.visible.filter { ($0.size ?? 0) > 0 }.sorted { ($0.size ?? 0) > ($1.size ?? 0) }
    }

    var body: some View {
        VStack(spacing: 8) {
            GeometryReader { geo in
                let shown = Array(items.prefix(150))
                let rects = Treemap.layout(shown.map { Double($0.size ?? 0) },
                                           in: CGRect(origin: .zero, size: geo.size))
                ZStack(alignment: .topLeading) {
                    ForEach(Array(shown.enumerated()), id: \.element.id) { i, e in
                        if i < rects.count { block(e, rects[i]) }
                    }
                    if shown.isEmpty {
                        Text(model.measuring ? tr("Measuring…") : tr("Nothing to show"))
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, maxHeight: .infinity)
                    }
                }
            }
            HStack(spacing: 14) {
                ForEach(Kind.allCases, id: \.self) { k in
                    HStack(spacing: 5) {
                        RoundedRectangle(cornerRadius: 2).fill(k.color).frame(width: 10, height: 10)
                        Text(k.label).font(.caption).foregroundStyle(.secondary)
                    }
                }
                Spacer()
            }
        }
        .padding(.horizontal, 24).padding(.bottom, 14)
    }

    private func block(_ e: Entry, _ r: CGRect) -> some View {
        let inner = r.insetBy(dx: 1, dy: 1)
        let w = max(inner.width, 0), h = max(inner.height, 0)
        let picked = model.selected.contains(e.id)
        return Button { open(e) } label: {
            ZStack(alignment: .topLeading) {
                RoundedRectangle(cornerRadius: 3).fill(Kind.of(e).color.opacity(e.isDir ? 0.95 : 0.85))
                if picked {                                   // selected: thick outline and a check mark
                    RoundedRectangle(cornerRadius: 3).strokeBorder(Color.white, lineWidth: 3)
                    RoundedRectangle(cornerRadius: 3).strokeBorder(Color.black.opacity(0.6), lineWidth: 1)
                    if w > 24 && h > 24 {
                        Image(systemName: "checkmark.circle.fill").foregroundStyle(.white)
                            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .topTrailing).padding(4)
                    }
                }
                if w > 64 && h > 34 {
                    VStack(alignment: .leading, spacing: 1) {
                        Text(e.name).font(.system(size: 11, weight: .medium)).lineLimit(1)
                        Text(fmt(e.size ?? 0)).font(.system(size: 10)).opacity(0.85)
                    }
                    .foregroundStyle(.white)
                    .padding(5)
                }
            }
            .frame(width: w, height: h)
        }
        .buttonStyle(.plain)
        .help("\(e.name) \u{2014} \(fmt(e.size ?? 0)). " + tr("Right-click to select or delete."))
        .contextMenu {
            Button(e.isDir ? tr("Open") : tr("Preview")) { open(e) }
            Button(picked ? tr("Deselect") : tr("Select")) { model.toggleSelected(e.id) }
            Divider()
            Button(model.deleteMode.label + "\u{2026}", role: .destructive) { model.pendingDelete = [e.id] }
        }
        .offset(x: inner.minX, y: inner.minY)
    }

    private func open(_ e: Entry) {
        if e.isDir { model.go(e.id) } else { model.preview = e.id }
    }
}
