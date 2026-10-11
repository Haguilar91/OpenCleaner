package com.opencleaner.android

import androidx.compose.ui.graphics.Color
import java.io.File

/** Squarified treemap layout: blocks stay close to square so they are easy to read. */
object Treemap {
    class Rect(val x: Float, val y: Float, val w: Float, val h: Float)

    /** [sizes] must be positive and sorted largest first. Returns one rect per size, in order. */
    fun layout(sizes: List<Double>, width: Float, height: Float): List<Rect> {
        val total = sizes.sum()
        if (total <= 0 || width <= 0f || height <= 0f) return emptyList()
        val scale = width.toDouble() * height / total
        val areas = sizes.map { it * scale }
        val out = ArrayList<Rect>(areas.size)
        var x = 0f; var y = 0f; var w = width; var h = height
        var i = 0
        while (i < areas.size) {
            val side = minOf(w, h).toDouble()
            var end = i + 1
            while (end < areas.size &&
                worst(areas.subList(i, end + 1), side) <= worst(areas.subList(i, end), side)) end++
            val row = areas.subList(i, end)
            val sum = row.sum()
            if (w >= h) {                       // column on the left
                val cw = (sum / h).toFloat()
                var cy = y
                for (a in row) { val ch = (a / cw).toFloat(); out.add(Rect(x, cy, cw, ch)); cy += ch }
                x += cw; w = maxOf(w - cw, 0f)
            } else {                            // row on the top
                val rh = (sum / w).toFloat()
                var cx = x
                for (a in row) { val rw = (a / rh).toFloat(); out.add(Rect(cx, y, rw, rh)); cx += rw }
                y += rh; h = maxOf(h - rh, 0f)
            }
            i = end
        }
        return out
    }

    private fun worst(row: List<Double>, side: Double): Double {
        val s = row.sum()
        val mx = row.max()
        val mn = row.min()
        if (s <= 0 || mn <= 0) return Double.MAX_VALUE
        return maxOf(side * side * mx / (s * s), s * s / (side * side * mn))
    }
}

/** File categories shown by color, like WinDirStat. */
enum class Kind(val label: String, val color: Color) {
    Folder("Folders", Color(0xFF2563EB)),
    Image("Images", Color(0xFF2E9E5B)),
    Video("Video", Color(0xFF8C54CC)),
    Audio("Audio", Color(0xFFE08524)),
    Document("Documents", Color(0xFF1A999E)),
    Archive("Archives", Color(0xFFC74545)),
    Other("Other", Color(0xFF6B758A));

    companion object {
        fun of(f: File): Kind {
            if (f.isDirectory) return Folder
            return when (f.extension.lowercase()) {
                "jpg", "jpeg", "png", "gif", "heic", "webp", "bmp", "dng", "raw" -> Image
                "mp4", "mov", "m4v", "mkv", "avi", "webm", "3gp" -> Video
                "mp3", "flac", "m4a", "aac", "wav", "ogg", "opus", "amr" -> Audio
                "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "csv", "rtf", "epub" -> Document
                "zip", "apk", "xapk", "iso", "tar", "gz", "7z", "rar", "obb" -> Archive
                else -> Other
            }
        }
    }
}
