package com.opencleaner.android

import android.content.Context
import android.os.Environment
import java.io.File
import java.nio.file.Files
import java.util.Locale

data class CleanItem(
    val id: String,
    val group: String,
    val title: String,
    val subtitle: String,
    val size: Long?,
    val selected: Boolean = false,
    val files: List<File> = emptyList(),
    val empty: Boolean = false,
)

data class CleanResult(val title: String, val skipped: Int, val error: String?)

fun fmt(bytes: Long?): String {
    if (bytes == null) return "size unknown"
    val units = arrayOf("B", "KB", "MB", "GB", "TB")
    var v = bytes.toDouble()
    var i = 0
    while (v >= 1000 && i < units.size - 1) { v /= 1000; i++ }
    return if (i == 0) "$bytes B" else String.format(Locale.US, "%.1f %s", v, units[i])
}

object Scanners {
    private const val MB = 1024L * 1024

    fun root(): File = Environment.getExternalStorageDirectory()

    /** Size of a file or folder tree. Symlinks count as zero. */
    fun size(f: File): Long {
        if (!f.exists()) return 0
        if (f.isFile) return f.length()
        if (Files.isSymbolicLink(f.toPath())) return 0
        var total = 0L
        f.listFiles()?.forEach { total += size(it) }
        return total
    }

    /**
     * Deletes are limited to shared storage and this app's own cache, and never touch
     * Android/data or Android/obb (other apps' private data).
     */
    fun allowed(f: File, ctx: Context): Boolean {
        val p = try { f.canonicalPath } catch (e: Exception) { return false }
        val own = listOfNotNull(ctx.cacheDir, ctx.externalCacheDir).map { it.canonicalPath }
        if (own.any { p.startsWith("$it/") }) return true
        val ext = root().canonicalPath
        if (!p.startsWith("$ext/")) return false
        if (p.startsWith("$ext/Android/data") || p.startsWith("$ext/Android/obb")) return false
        return true
    }

    private class ScanDef(val group: String, val label: String, val fn: () -> List<CleanItem>)

    fun runAll(ctx: Context): List<CleanItem> {
        val defs = listOf(
            ScanDef("This app", "OpenCleaner's own cache") { ownCache(ctx) },
            ScanDef("Junk files", "Thumbnail leftovers") { thumbnails() },
            ScanDef("Junk files", "Old installer files (.apk) in Downloads") { oldApks() },
            ScanDef("Messaging apps", "WhatsApp sent-media copies") { whatsappSent() },
            ScanDef("Large files", "Files over 100 MB") { largeFiles() },
        )
        val out = mutableListOf<CleanItem>()
        for (d in defs) {
            try {
                val found = d.fn()
                if (found.isEmpty()) {
                    out += CleanItem("empty:${d.label}", d.group, d.label, "Nothing to clean", 0, empty = true)
                } else out += found
            } catch (e: Exception) {
                out += CleanItem("fail:${d.label}", d.group, d.label, "Scan failed: ${e.message}", null, empty = true)
            }
        }
        return out
    }

    fun clean(ctx: Context, item: CleanItem): CleanResult {
        var skipped = 0
        var error: String? = null
        for (f in item.files) {
            if (!allowed(f, ctx)) { error = "Refusing to delete ${f.path}"; break }
            if (!f.deleteRecursively()) skipped++
        }
        return CleanResult(item.title, skipped, error)
    }

    private fun item(id: String, group: String, title: String, subtitle: String,
                     files: List<File>, selected: Boolean = false): CleanItem? {
        val size = files.sumOf { size(it) }
        return if (files.isEmpty() || size == 0L) null
        else CleanItem(id, group, title, subtitle, size, selected, files)
    }

    private fun ownCache(ctx: Context): List<CleanItem> {
        val kids = listOfNotNull(ctx.cacheDir, ctx.externalCacheDir).flatMap { it.listFiles()?.toList() ?: emptyList() }
        return listOfNotNull(item("own-cache", "This app", "OpenCleaner's own cache",
            "Temporary files this app created", kids, selected = true))
    }

    private fun thumbnails(): List<CleanItem> {
        val dir = File(root(), "DCIM/.thumbnails")
        val kids = dir.listFiles()?.toList() ?: emptyList()
        return listOfNotNull(item("thumbs", "Junk files", "Thumbnail leftovers",
            "Old gallery thumbnails in DCIM/.thumbnails. Rebuilt on demand.", kids, selected = true))
    }

    private fun oldApks(): List<CleanItem> {
        val dl = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val apks = dl.walkTopDown().maxDepth(2).filter { it.isFile && it.extension.equals("apk", true) }.toList()
        return listOfNotNull(item("apks", "Junk files", "Old installer files (${apks.size})",
            "APKs left in Downloads after installing. Keep any you still need.", apks))
    }

    private fun whatsappSent(): List<CleanItem> {
        val bases = listOf(
            File(root(), "Android/media/com.whatsapp/WhatsApp/Media"),
            File(root(), "WhatsApp/Media"),
        )
        val kinds = listOf("WhatsApp Images", "WhatsApp Video", "WhatsApp Documents", "WhatsApp Audio")
        val files = bases.flatMap { b -> kinds.map { File(b, "$it/Sent") } }
            .flatMap { it.listFiles()?.toList() ?: emptyList() }
        return listOfNotNull(item("wa-sent", "Messaging apps", "WhatsApp sent-media copies",
            "Copies of media you sent. If you deleted the original from your gallery, this may be the only copy.", files))
    }

    private fun largeFiles(): List<CleanItem> {
        val min = 100 * MB
        val found = ArrayList<File>()
        fun walk(d: File, depth: Int) {
            if (depth > 8) return
            val kids = d.listFiles() ?: return
            for (k in kids) {
                if (depth == 0 && k.name == "Android") continue
                if (k.isDirectory) {
                    if (!Files.isSymbolicLink(k.toPath())) walk(k, depth + 1)
                } else if (k.length() >= min) found.add(k)
            }
        }
        walk(root(), 0)
        val rootPath = root().path
        return found.sortedByDescending { it.length() }.take(15).map {
            CleanItem("big:${it.path}", "Large files", it.name,
                it.parent?.removePrefix(rootPath).orEmpty().ifEmpty { "/" },
                it.length(), false, listOf(it))
        }
    }
}
