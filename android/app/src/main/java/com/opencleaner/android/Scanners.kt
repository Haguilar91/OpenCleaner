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

/** Large files the user chose to hide: kept out of the Large files list on every scan (saved in prefs). */
object HiddenFiles {
    private const val KEY = "hidden_large_files"
    const val ROW_ID = "hidden-large-files"
    private var prefs: android.content.SharedPreferences? = null

    fun init(ctx: Context) {
        if (prefs == null) prefs = ctx.applicationContext.getSharedPreferences("settings", Context.MODE_PRIVATE)
    }

    fun all(): List<String> = (prefs?.getStringSet(KEY, emptySet()) ?: emptySet()).sorted()
    fun contains(path: String) = path in all()
    fun add(path: String) = save(all().toSet() + path)
    fun remove(path: String) = save(all().toSet() - path)
    private fun save(set: Set<String>) { prefs?.edit()?.putStringSet(KEY, set)?.apply() }
}

data class CleanResult(val title: String, val skipped: Int, val error: String?)

/** Storage tab state that survives switching tabs (the screen itself is rebuilt each time).
 *  Sizes are kept for the session and cleared after any delete or clean. */
object StorageMemory {
    var path: File? = null
    var mapMode = false
    val sizes = java.util.concurrent.ConcurrentHashMap<String, Long>()
}

fun fmt(bytes: Long?): String {
    if (bytes == null) return tr("size unknown")
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
        HiddenFiles.init(ctx)
        val defs = listOf(
            ScanDef(tr("This app"), tr("OpenCleaner's own cache")) { ownCache(ctx) },
            ScanDef(tr("Junk files"), tr("Thumbnail leftovers")) { thumbnails() },
            ScanDef(tr("Junk files"), tr("Old installer files (.apk) in Downloads")) { oldApks() },
            ScanDef(tr("Junk files"), tr("Gallery trash")) { galleryTrash() },
            ScanDef(tr("Junk files"), tr("Duplicate downloads")) { duplicateDownloads() },
            ScanDef(tr("Messaging apps"), tr("WhatsApp sent-media copies")) { whatsappSent() },
            ScanDef(tr("Large files"), tr("Files over 100 MB")) { largeFiles() },
        )
        val out = mutableListOf<CleanItem>()
        for (d in defs) {
            try {
                val found = d.fn()
                if (found.isEmpty()) {
                    out += CleanItem("empty:${d.label}", d.group, d.label, tr("Nothing to clean"), 0, empty = true)
                } else out += found
            } catch (e: Exception) {
                out += CleanItem("fail:${d.label}", d.group, d.label, tr("Scan failed: {0}", e.message), null, empty = true)
            }
        }
        return withHiddenRow(out)
    }

    /** The "Hidden files (N)" row at the end of the Large files group (none when nothing is hidden). */
    fun withHiddenRow(items: List<CleanItem>): List<CleanItem> {
        val rest = items.filter { it.id != HiddenFiles.ROW_ID }
        val n = HiddenFiles.all().size
        if (n == 0) return rest
        val at = rest.indexOfLast { it.group == tr("Large files") } + 1
        val row = CleanItem(HiddenFiles.ROW_ID, tr("Large files"), tr("Hidden files ({0})", n),
            tr("Kept out of this list so they are not deleted by mistake."), null, empty = true)
        return if (at == 0) rest + row else rest.take(at) + row + rest.drop(at)
    }

    /** Hides one large file: drops its row (an empty Large files group gets its "Clean" row back). */
    fun hide(items: List<CleanItem>, item: CleanItem): List<CleanItem> {
        item.files.firstOrNull()?.let { HiddenFiles.add(it.path) }
        val at = items.indexOf(item)
        var out = items - item
        if (out.none { it.group == tr("Large files") && !it.empty }) {
            val emptyRow = CleanItem("empty:" + tr("Files over 100 MB"), tr("Large files"), tr("Files over 100 MB"),
                tr("Nothing to clean"), 0, empty = true)
            out = out.take(at) + emptyRow + out.drop(at)
        }
        return withHiddenRow(out)
    }

    fun clean(ctx: Context, item: CleanItem): CleanResult {
        var skipped = 0
        var error: String? = null
        for (f in item.files) {
            if (!allowed(f, ctx)) { error = tr("Refusing to delete {0}", f.path); break }
            if (!f.deleteRecursively()) {
                // count what is left (in use or protected), not just "1 folder"
                skipped += if (f.isDirectory) f.walkBottomUp().count { it.isFile }.coerceAtLeast(1) else 1
            }
        }
        StorageMemory.sizes.clear()
        return CleanResult(item.title, skipped, error)
    }

    /** Files the gallery moved to its trash (".trashed-<time>-name", kept 30 days by Android 11+). */
    private fun galleryTrash(): List<CleanItem> {
        val dirs = listOf("DCIM", "Pictures", "Movies", "Download", "Music", "Documents").map { File(root(), it) }
        val files = dirs.flatMap { d ->
            d.walkTopDown().maxDepth(4).filter { it.isFile && it.name.startsWith(".trashed-") }.toList()
        }
        return listOfNotNull(item("gallery-trash", tr("Junk files"), tr("Gallery trash ({0})", files.size),
            tr("Photos and videos you deleted in the gallery. Android removes them after 30 days."), files))
    }

    /** Files in Download that are byte-for-byte copies of another file there; the oldest copy is kept. */
    private fun duplicateDownloads(): List<CleanItem> {
        val dl = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val files = dl.walkTopDown().maxDepth(3).filter { it.isFile && it.length() >= MB }.toList()
        val out = ArrayList<CleanItem>()
        for (sameSize in files.groupBy { it.length() }.values.filter { it.size > 1 }) {
            for (sameData in sameSize.groupBy { sha256(it) }.filterKeys { it != null }.values.filter { it.size > 1 }) {
                val keep = sameData.minByOrNull { it.lastModified() } ?: continue
                for (copy in sameData.filter { it != keep }) {
                    out += CleanItem("dup:${copy.path}", tr("Junk files"), copy.name,
                        tr("Same as {0}, which is kept.", keep.name), copy.length(), false, listOf(copy))
                }
            }
        }
        return out.sortedByDescending { it.size ?: 0 }.take(30)
    }

    private fun sha256(f: File): String? = try {
        val md = java.security.MessageDigest.getInstance("SHA-256")
        f.inputStream().use { input ->
            val buf = ByteArray(1 shl 16)
            while (true) {
                val n = input.read(buf)
                if (n < 0) break
                md.update(buf, 0, n)
            }
        }
        md.digest().joinToString("") { "%02x".format(it) }
    } catch (e: Exception) { null }

    private fun item(id: String, group: String, title: String, subtitle: String,
                     files: List<File>, selected: Boolean = false): CleanItem? {
        val size = files.sumOf { size(it) }
        return if (files.isEmpty() || size == 0L) null
        else CleanItem(id, group, title, subtitle, size, selected, files)
    }

    private fun ownCache(ctx: Context): List<CleanItem> {
        val kids = listOfNotNull(ctx.cacheDir, ctx.externalCacheDir).flatMap { it.listFiles()?.toList() ?: emptyList() }
        return listOfNotNull(item("own-cache", tr("This app"), tr("OpenCleaner's own cache"),
            tr("Temporary files this app created"), kids, selected = true))
    }

    private fun thumbnails(): List<CleanItem> {
        val dir = File(root(), "DCIM/.thumbnails")
        val kids = dir.listFiles()?.toList() ?: emptyList()
        return listOfNotNull(item("thumbs", tr("Junk files"), tr("Thumbnail leftovers"),
            tr("Old gallery thumbnails in DCIM/.thumbnails. Rebuilt on demand."), kids, selected = true))
    }

    private fun oldApks(): List<CleanItem> {
        val dl = Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_DOWNLOADS)
        val apks = dl.walkTopDown().maxDepth(2).filter { it.isFile && it.extension.equals("apk", true) }.toList()
        return listOfNotNull(item("apks", tr("Junk files"), tr("Old installer files ({0})", apks.size),
            tr("APKs left in Downloads after installing. Keep any you still need."), apks))
    }

    private fun whatsappSent(): List<CleanItem> {
        val bases = listOf(
            File(root(), "Android/media/com.whatsapp/WhatsApp/Media"),
            File(root(), "WhatsApp/Media"),
        )
        val kinds = listOf("WhatsApp Images", "WhatsApp Video", "WhatsApp Documents", "WhatsApp Audio")
        val files = bases.flatMap { b -> kinds.map { File(b, "$it/Sent") } }
            .flatMap { it.listFiles()?.toList() ?: emptyList() }
        return listOfNotNull(item("wa-sent", tr("Messaging apps"), tr("WhatsApp sent-media copies"),
            tr("Copies of media you sent. If you deleted the original from your gallery, this may be the only copy."), files))
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
                } else if (k.length() >= min && !HiddenFiles.contains(k.path)) found.add(k)
            }
        }
        walk(root(), 0)
        val rootPath = root().path
        return found.sortedByDescending { it.length() }.take(15).map {
            CleanItem("big:${it.path}", tr("Large files"), it.name,
                it.parent?.removePrefix(rootPath).orEmpty().ifEmpty { "/" },
                it.length(), false, listOf(it))
        }
    }
}
