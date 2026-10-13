package com.opencleaner.android

import android.content.Context
import android.content.Intent
import androidx.core.content.FileProvider
import org.json.JSONObject
import java.io.File
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/** A GitHub release of OpenCleaner. */
data class Release(val version: String, val notes: String, val page: String, val assets: Map<String, String>)

/**
 * Updates from the GitHub releases of Haguilar91/OpenCleaner: check, download the APK, verify it against the
 * release's SHA256SUMS.txt, then hand it to Android's installer (which asks the user to confirm).
 */
object Updater {
    private const val API = "https://api.github.com/repos/Haguilar91/OpenCleaner/releases/latest"
    private val APK_SUFFIX = BuildConfig.APK_SUFFIX     // "-android.apk" (phone) or "-androidtv.apk" (TV)

    fun currentVersion(ctx: Context): String =
        ctx.packageManager.getPackageInfo(ctx.packageName, 0).versionName ?: "0"

    private fun parts(v: String): List<Int> =
        v.trim().trimStart('v', 'V').split('.').map { p -> p.takeWhile { it.isDigit() }.toIntOrNull() ?: 0 }

    fun isNewer(a: String, b: String): Boolean {
        val x = parts(a); val y = parts(b)
        for (i in 0 until maxOf(x.size, y.size)) {
            val p = x.getOrElse(i) { 0 }; val q = y.getOrElse(i) { 0 }
            if (p != q) return p > q
        }
        return false
    }

    private fun get(url: String): HttpURLConnection =
        (URL(url).openConnection() as HttpURLConnection).apply {
            connectTimeout = 20_000; readTimeout = 60_000
            setRequestProperty("Accept", "application/vnd.github+json")
            setRequestProperty("User-Agent", "OpenCleaner-Android")
        }

    /** Blocking: call from a background thread. */
    fun latest(): Release {
        val json = JSONObject(get(API).inputStream.bufferedReader().use { it.readText() })
        val assets = mutableMapOf<String, String>()
        val arr = json.optJSONArray("assets")
        if (arr != null) for (i in 0 until arr.length()) {
            val a = arr.getJSONObject(i)
            assets[a.getString("name")] = a.getString("browser_download_url")
        }
        return Release(json.getString("tag_name").trimStart('v', 'V'), json.optString("body"),
            json.getString("html_url"), assets)
    }

    /** Blocking: downloads the APK into the app's cache and checks it. Returns the file, or throws. */
    fun download(ctx: Context, r: Release): File {
        // phone and TV builds are separate APKs: pick ours by name ("…-android.apk" / "…-androidtv.apk")
        val name = r.assets.keys.firstOrNull { it.endsWith(APK_SUFFIX) }
            ?: throw IllegalStateException("This release has no Android download.")
        val sumsUrl = r.assets["SHA256SUMS.txt"]
            ?: throw IllegalStateException("This release has no checksums, so it was not installed.")
        val sums = get(sumsUrl).inputStream.bufferedReader().use { it.readText() }
        val dir = File(ctx.cacheDir, "updates").apply { mkdirs() }
        dir.listFiles()?.forEach { it.delete() }
        val apk = File(dir, name)
        val md = MessageDigest.getInstance("SHA-256")
        get(r.assets.getValue(name)).inputStream.use { input ->
            apk.outputStream().use { out ->
                val buf = ByteArray(1 shl 16)
                while (true) {
                    val n = input.read(buf); if (n < 0) break
                    md.update(buf, 0, n); out.write(buf, 0, n)
                }
            }
        }
        val hex = md.digest().joinToString("") { "%02x".format(it) }
        if (sums.lines().none { it.startsWith(hex) && it.endsWith(name) }) {
            apk.delete()
            throw IllegalStateException("The download did not match the release checksum, so it was not installed.")
        }
        return apk
    }

    /** Opens Android's installer for the downloaded APK; Android asks the user to confirm. */
    fun install(ctx: Context, apk: File) {
        val uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".fileprovider", apk)
        ctx.startActivity(Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK))
    }
}
