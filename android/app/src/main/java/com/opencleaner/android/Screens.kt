package com.opencleaner.android

import android.app.AppOpsManager
import android.app.usage.StorageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.net.Uri
import android.os.Process
import android.os.storage.StorageManager
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.TextButton
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.File

private val Green = Color(0xFF2E9E5B)

// ---------------------------------------------------------------- permission
@Composable
fun PermissionScreen() {
    val ctx = LocalContext.current
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text("OpenCleaner needs file access", style = MaterialTheme.typography.headlineSmall)
        Text(
            "\nTo find and delete junk files on your phone, Android requires you to allow " +
                "\"All files access\". The app only deletes items you tick and confirm, " +
                "and never touches other apps' private data.\n",
            style = MaterialTheme.typography.bodyMedium,
        )
        Button(onClick = {
            val i = Intent(Settings.ACTION_MANAGE_APP_ALL_FILES_ACCESS_PERMISSION,
                Uri.parse("package:${ctx.packageName}"))
            try { ctx.startActivity(i) } catch (e: Exception) {
                ctx.startActivity(Intent(Settings.ACTION_MANAGE_ALL_FILES_ACCESS_PERMISSION))
            }
        }) { Text("Open settings") }
    }
}

// ---------------------------------------------------------------- clean
@Composable
fun CleanScreen(vm: CleanVm) {
    val groups = vm.items.groupBy { it.group }
    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            LazyColumn(Modifier.weight(1f)) {
                groups.forEach { (g, list) ->
                    item(key = "h:$g") {
                        Text(g, style = MaterialTheme.typography.titleSmall,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.padding(16.dp, 12.dp, 16.dp, 4.dp))
                    }
                    items(list, key = { it.id }) { CleanRow(vm, it) }
                }
            }
            HorizontalDivider()
            Row(
                Modifier.fillMaxWidth().padding(12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                TextButton(onClick = { vm.scan() }, enabled = !vm.busy) { Text("Rescan") }
                val known = vm.chosen.sumOf { it.size ?: 0 }
                Text("${vm.chosen.size} selected · ${fmt(known)}",
                    modifier = Modifier.weight(1f),
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                Button(onClick = { vm.confirming = true },
                    enabled = vm.chosen.isNotEmpty() && !vm.busy) { Text("Clean") }
            }
        }
        if (vm.busy) {
            Column(Modifier.align(Alignment.Center), horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator()
                Text(vm.busyText, modifier = Modifier.padding(top = 8.dp))
            }
        }
    }

    if (vm.confirming) {
        AlertDialog(
            onDismissRequest = { vm.confirming = false },
            title = { Text("Delete selected items?") },
            text = {
                Text("This cannot be undone.\n\n" +
                    vm.chosen.joinToString("\n") { "• ${it.title} (${fmt(it.size)})" })
            },
            confirmButton = { TextButton(onClick = { vm.confirming = false; vm.clean() }) { Text("Clean") } },
            dismissButton = { TextButton(onClick = { vm.confirming = false }) { Text("Cancel") } },
        )
    }
    vm.message?.let { msg ->
        AlertDialog(
            onDismissRequest = { vm.message = null },
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = { vm.message = null }) { Text("OK") } },
        )
    }
}

@Composable
private fun CleanRow(vm: CleanVm, item: CleanItem) {
    Row(
        Modifier.fillMaxWidth()
            .clickable(enabled = !item.empty) { vm.toggle(item.id) }
            .padding(horizontal = 16.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (item.empty) {
            Text("✓", color = Green, modifier = Modifier.width(48.dp).padding(start = 14.dp))
        } else {
            Checkbox(checked = item.id in vm.selected, onCheckedChange = { vm.toggle(item.id) })
        }
        Column(Modifier.weight(1f).padding(horizontal = 8.dp)) {
            Text(item.title, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(item.subtitle, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2,
                overflow = TextOverflow.Ellipsis)
        }
        if (item.empty) Text("Clean ✓", color = Green)
        else Text(fmt(item.size), color = MaterialTheme.colorScheme.onSurfaceVariant)
    }
}

// ---------------------------------------------------------------- storage browser
private data class Entry(val file: File, val size: Long?)

@Composable
fun StorageScreen(onChanged: () -> Unit) {
    val ctx = LocalContext.current
    val root = remember { Scanners.root() }
    var path by remember { mutableStateOf(root) }
    var entries by remember { mutableStateOf<List<Entry>>(emptyList()) }
    var reload by remember { mutableIntStateOf(0) }
    var toDelete by remember { mutableStateOf<File?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    val scope = androidx.compose.runtime.rememberCoroutineScope()

    BackHandler(enabled = path != root) { path = path.parentFile ?: root }

    LaunchedEffect(path, reload) {
        val kids = withContext(Dispatchers.IO) { path.listFiles()?.toList() ?: emptyList() }
        entries = kids.map { Entry(it, null) }
        coroutineScope {
            val sem = Semaphore(4)
            for (f in kids) {
                launch(Dispatchers.IO) {
                    sem.withPermit {
                        val s = Scanners.size(f)
                        withContext(Dispatchers.Main) {
                            entries = entries.map { if (it.file == f) it.copy(size = s) else it }
                                .sortedByDescending { it.size ?: -1 }
                        }
                    }
                }
            }
        }
    }

    val total = entries.sumOf { it.size ?: 0 }
    val maxSize = (entries.maxOfOrNull { it.size ?: 0 } ?: 1).coerceAtLeast(1)

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            TextButton(onClick = { path = path.parentFile ?: root }, enabled = path != root) { Text("Up") }
            Text(path.path.removePrefix(root.path).ifEmpty { "/" }.let { "Internal storage$it" },
                modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Text("Total ${fmt(total)}", style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 2.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        HorizontalDivider()
        LazyColumn(Modifier.fillMaxSize()) {
            items(entries, key = { it.file.path }) { e ->
                Column(
                    Modifier.fillMaxWidth()
                        .clickable(enabled = e.file.isDirectory) { path = e.file }
                        .padding(horizontal = 16.dp, vertical = 6.dp)
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Text(if (e.file.isDirectory) "📁 " else "📄 ")
                        Text(e.file.name, modifier = Modifier.weight(1f), maxLines = 1,
                            overflow = TextOverflow.Ellipsis)
                        Text(if (e.size == null) "…" else fmt(e.size),
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                        TextButton(onClick = { toDelete = e.file }) { Text("Delete") }
                    }
                    LinearProgressIndicator(
                        progress = { (e.size ?: 0).toFloat() / maxSize },
                        modifier = Modifier.fillMaxWidth(),
                    )
                }
            }
        }
    }

    toDelete?.let { f ->
        AlertDialog(
            onDismissRequest = { toDelete = null },
            title = { Text("Delete permanently?") },
            text = { Text("${f.name}\n\nAndroid has no trash for files, so this cannot be undone.") },
            confirmButton = {
                TextButton(onClick = {
                    toDelete = null
                    scope.launch {
                        val ok = withContext(Dispatchers.IO) {
                            Scanners.allowed(f, ctx) && f.deleteRecursively()
                        }
                        if (!ok) error = "Could not delete ${f.name}"
                        reload++
                        onChanged()
                    }
                }) { Text("Delete") }
            },
            dismissButton = { TextButton(onClick = { toDelete = null }) { Text("Cancel") } },
        )
    }
    error?.let { msg ->
        AlertDialog(
            onDismissRequest = { error = null },
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = { error = null }) { Text("OK") } },
        )
    }
}

// ---------------------------------------------------------------- apps
private data class AppRow(val label: String, val pkg: String, val total: Long, val cache: Long, val data: Long)

private fun hasUsageAccess(ctx: Context): Boolean {
    val ops = ctx.getSystemService(AppOpsManager::class.java)
    val mode = ops.unsafeCheckOpNoThrow(AppOpsManager.OPSTR_GET_USAGE_STATS, Process.myUid(), ctx.packageName)
    return mode == AppOpsManager.MODE_ALLOWED
}

private fun loadApps(ctx: Context): List<AppRow> {
    val pm = ctx.packageManager
    val ssm = ctx.getSystemService(StorageStatsManager::class.java)
    return pm.getInstalledApplications(0)
        .filter { it.flags and ApplicationInfo.FLAG_SYSTEM == 0 || it.flags and ApplicationInfo.FLAG_UPDATED_SYSTEM_APP != 0 }
        .mapNotNull { a ->
            try {
                val st = ssm.queryStatsForPackage(StorageManager.UUID_DEFAULT, a.packageName, Process.myUserHandle())
                AppRow(pm.getApplicationLabel(a).toString(), a.packageName,
                    st.appBytes + st.dataBytes + st.cacheBytes, st.cacheBytes, st.dataBytes)
            } catch (e: Exception) { null }
        }
        .sortedByDescending { it.total }
}

@Composable
fun AppsScreen() {
    val ctx = LocalContext.current
    var access by remember { mutableStateOf(hasUsageAccess(ctx)) }
    var apps by remember { mutableStateOf<List<AppRow>?>(null) }

    androidx.lifecycle.compose.LifecycleEventEffect(androidx.lifecycle.Lifecycle.Event.ON_RESUME) {
        access = hasUsageAccess(ctx)
    }
    LaunchedEffect(access) {
        apps = if (access) withContext(Dispatchers.IO) { loadApps(ctx) } else null
    }

    if (!access) {
        Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally) {
            Text("Allow usage access", style = MaterialTheme.typography.titleMedium)
            Text("\nAndroid only shares each app's size if you allow \"Usage access\". " +
                "OpenCleaner only reads sizes. It cannot see what you do in apps.\n")
            Button(onClick = { ctx.startActivity(Intent(Settings.ACTION_USAGE_ACCESS_SETTINGS)) }) {
                Text("Open settings")
            }
        }
        return
    }
    val list = apps
    if (list == null) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        return
    }
    Column(Modifier.fillMaxSize()) {
        Text("Tap an app to open its settings, where you can clear its cache or uninstall it.",
            style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.padding(16.dp, 8.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        HorizontalDivider()
        LazyColumn(Modifier.fillMaxSize()) {
            items(list, key = { it.pkg }) { a ->
                Row(
                    Modifier.fillMaxWidth().clickable {
                        ctx.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                            Uri.parse("package:${a.pkg}")))
                    }.padding(horizontal = 16.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(a.label, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text("data ${fmt(a.data)} · cache ${fmt(a.cache)}",
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Text(fmt(a.total))
                }
            }
        }
    }
}
