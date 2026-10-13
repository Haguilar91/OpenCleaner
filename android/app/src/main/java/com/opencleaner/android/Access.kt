package com.opencleaner.android

import android.Manifest
import android.app.AppOpsManager
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.os.Process
import android.provider.Settings
import androidx.compose.foundation.border
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.composed
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/**
 * Permissions that differ between Android versions and between phones and TVs.
 * Android 11+ uses "All files access"; Android 9-10 (Fire OS 7) the classic storage permission.
 * Some TVs have no Settings page for "All files access" or "Usage access"; then the user can grant it
 * once with adb, and the app shows the exact command.
 */
object Access {
    val legacyStorage = arrayOf(Manifest.permission.READ_EXTERNAL_STORAGE, Manifest.permission.WRITE_EXTERNAL_STORAGE)

    fun hasStorage(ctx: Context): Boolean =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) Environment.isExternalStorageManager()
        else legacyStorage.all { ctx.checkSelfPermission(it) == PackageManager.PERMISSION_GRANTED }

    /** Opens the "All files access" page; false when the device has none (some TVs). */
    fun openAllFilesSettings(ctx: Context): Boolean {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return false
        return tryStart(ctx, Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
            Uri.parse("package:${ctx.packageName}"))) ||
            tryStart(ctx, Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
    }

    fun hasUsageAccess(ctx: Context): Boolean {
        val ops = ctx.getSystemService(AppOpsManager::class.java)
        val mode = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q)
            ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), ctx.packageName)
        else @Suppress("DEPRECATION")
            ops.checkOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), ctx.packageName)
        return mode == AppOpsManager.MODE_ALLOWED
    }

    /** Opens the "Usage access" page; false when the device has none (some TVs). */
    fun openUsageSettings(ctx: Context): Boolean = tryStart(ctx, Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS))

    fun adbCommand(ctx: Context, op: String) = "adb shell appops set ${ctx.packageName} $op allow"

    private fun tryStart(ctx: Context, i: Intent): Boolean = try {
        if (i.resolveActivity(ctx.packageManager) == null) false else { ctx.startActivity(i); true }
    } catch (e: Exception) { false }
}

/** On TV, a clear outline around whatever the remote has focused. Does nothing on phones. */
fun Modifier.focusRing(): Modifier = composed {
    if (!BuildConfig.IS_TV) return@composed this
    var focused by remember { mutableStateOf(false) }
    this.onFocusChanged { focused = it.isFocused || it.hasFocus }
        .border(if (focused) 3.dp else 0.dp,
            if (focused) MaterialTheme.colorScheme.primary else Color.Transparent, RoundedCornerShape(8.dp))
}
