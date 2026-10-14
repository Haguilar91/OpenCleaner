package com.opencleaner.android

import android.app.AppOpsManager
import android.app.usage.StorageStatsManager
import android.text.format.DateUtils
import android.app.usage.UsageStatsManager
import android.content.Context
import android.content.Intent
import android.content.pm.ApplicationInfo
import android.graphics.Bitmap
import android.graphics.BitmapFactory
import android.media.ThumbnailUtils
import android.net.Uri
import androidx.compose.ui.window.DialogProperties
import androidx.compose.ui.viewinterop.AndroidView
import android.widget.VideoView
import android.util.Size
import android.webkit.MimeTypeMap
import android.os.Process
import android.os.storage.StorageManager
import android.provider.Settings
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.Image
import androidx.compose.foundation.clickable
import androidx.compose.foundation.border
import androidx.compose.foundation.combinedClickable
import androidx.compose.foundation.ExperimentalFoundationApi
import kotlin.math.roundToInt
import androidx.compose.ui.unit.Dp
import androidx.compose.ui.unit.IntOffset
import androidx.compose.ui.platform.LocalDensity
import androidx.compose.ui.draw.clip
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.offset
import androidx.compose.foundation.layout.BoxWithConstraints
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import androidx.compose.runtime.produceState
import androidx.compose.ui.graphics.asImageBitmap
import androidx.compose.ui.layout.ContentScale
import androidx.core.content.FileProvider
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.Button
import androidx.compose.material3.Checkbox
import androidx.compose.material3.CircularProgressIndicator
import androidx.compose.material3.DropdownMenu
import androidx.compose.material3.DropdownMenuItem
import androidx.compose.material3.HorizontalDivider
import androidx.compose.material3.Icon
import androidx.compose.material3.IconButton
import androidx.compose.material3.LinearProgressIndicator
import androidx.compose.material3.LocalContentColor
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.Slider
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
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.painterResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.delay
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.File

private val Green = Color(0xFF2E9E5B)

/** Minimal Lucide outline icon (see res/drawable/ic_*.xml), tinted with the current content color. */
@Composable
fun Ico(res: Int, desc: String? = null, tint: Color = LocalContentColor.current, size: Int = 20) {
    Icon(painterResource(res), desc, Modifier.size(size.dp), tint = tint)
}

// ---------------------------------------------------------------- permission
@Composable
fun PermissionScreen(onGranted: () -> Unit = {}) {
    val ctx = LocalContext.current
    var noSettings by remember { mutableStateOf(false) }
    // Android 9-10 (older Fire TV): the classic storage permission, asked right here
    val legacy = androidx.activity.compose.rememberLauncherForActivityResult(
        androidx.activity.result.contract.ActivityResultContracts.RequestMultiplePermissions()) { onGranted() }
    Column(
        Modifier.fillMaxSize().padding(24.dp),
        verticalArrangement = Arrangement.Center,
        horizontalAlignment = Alignment.CenterHorizontally,
    ) {
        Text(tr("OpenCleaner needs file access"), style = MaterialTheme.typography.headlineSmall)
        Text(
            "\n" + tr("To find and delete junk files, Android requires you to allow " +
                "\"All files access\". The app only deletes items you tick and confirm, " +
                "and never touches other apps' private data.") + "\n",
            style = MaterialTheme.typography.bodyMedium,
        )
        Button(modifier = Modifier.focusRing(), onClick = {
            if (android.os.Build.VERSION.SDK_INT < android.os.Build.VERSION_CODES.R) legacy.launch(Access.legacyStorage)
            else if (!Access.openAllFilesSettings(ctx)) noSettings = true
        }) { Text(if (android.os.Build.VERSION.SDK_INT < android.os.Build.VERSION_CODES.R) tr("Allow access") else tr("Open settings")) }
        if (noSettings) {
            Text("\n" + tr("This device has no \"All files access\" settings page (common on TVs). Grant it once from a " +
                "computer with adb:") + "\n\n" + Access.adbCommand(ctx, "MANAGE_EXTERNAL_STORAGE") +
                "\n\n" + tr("then reopen OpenCleaner."), style = MaterialTheme.typography.bodyMedium)
        }
    }
}

// ---------------------------------------------------------------- clean
@Composable
fun CleanScreen(vm: CleanVm) {
    val groups = vm.items.groupBy { it.group }
    var reviewing by remember { mutableStateOf<CleanItem?>(null) }
    Box(Modifier.fillMaxSize()) {
        Column(Modifier.fillMaxSize()) {
            LazyColumn(Modifier.weight(1f)) {
                groups.forEach { (g, list) ->
                    item(key = "h:$g") {
                        Text(g, style = MaterialTheme.typography.titleSmall,
                            color = MaterialTheme.colorScheme.primary,
                            modifier = Modifier.padding(16.dp, 12.dp, 16.dp, 4.dp))
                    }
                    items(list, key = { it.id }) { CleanRow(vm, it) { reviewing = it } }
                }
            }
            HorizontalDivider()
            Row(
                Modifier.fillMaxWidth().padding(12.dp),
                verticalAlignment = Alignment.CenterVertically,
                horizontalArrangement = Arrangement.spacedBy(8.dp),
            ) {
                IconButton(onClick = { vm.scan() }, enabled = !vm.busy) { Ico(R.drawable.ic_refresh, tr("Rescan")) }
                val known = vm.chosen.sumOf { it.size ?: 0 }
                Text(tr("{0} selected · {1}", vm.chosen.size, fmt(known)),
                    modifier = Modifier.weight(1f),
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                Button(onClick = { vm.confirming = true },
                    enabled = vm.chosen.isNotEmpty() && !vm.busy) { Text(tr("Clean")) }
            }
        }
        if (vm.busy) {
            Column(Modifier.align(Alignment.Center), horizontalAlignment = Alignment.CenterHorizontally) {
                CircularProgressIndicator()
                Text(vm.busyText, modifier = Modifier.padding(top = 8.dp))
            }
        }
    }

    reviewing?.let { ReviewDialog(it) { reviewing = null } }
    if (vm.confirming) {
        AlertDialog(
            onDismissRequest = { vm.confirming = false },
            title = { Text(tr("Delete selected items?")) },
            text = {
                Text(tr("This cannot be undone.") + "\n\n" +
                    vm.chosen.joinToString("\n") { "• ${it.title} (${fmt(it.size)})" })
            },
            confirmButton = { TextButton(onClick = { vm.confirming = false; vm.clean() }) { Text(tr("Clean")) } },
            dismissButton = { TextButton(onClick = { vm.confirming = false }) { Text(tr("Cancel")) } },
        )
    }
    vm.message?.let { msg ->
        AlertDialog(
            onDismissRequest = { vm.message = null },
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = { vm.message = null }) { Text(tr("OK")) } },
        )
    }
}

@Composable
private fun CleanRow(vm: CleanVm, item: CleanItem, onReview: () -> Unit) {
    Row(
        Modifier.fillMaxWidth()
            .focusRing()
            .clickable(enabled = !item.empty) { vm.toggle(item.id) }
            .padding(horizontal = 16.dp, vertical = 6.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        if (item.empty) {
            Box(Modifier.width(48.dp), contentAlignment = Alignment.Center) { Ico(R.drawable.ic_check, tint = Green) }
        } else {
            Checkbox(checked = item.id in vm.selected, onCheckedChange = { vm.toggle(item.id) })
        }
        Column(Modifier.weight(1f).padding(horizontal = 8.dp)) {
            Text(item.title, maxLines = 1, overflow = TextOverflow.Ellipsis)
            Text(item.subtitle, style = MaterialTheme.typography.bodySmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 2,
                overflow = TextOverflow.Ellipsis)
        }
        if (item.empty) { Ico(R.drawable.ic_check, tint = Green, size = 16); Text(" " + tr("status|Clean"), color = Green) }
        else {
            Text(fmt(item.size), color = MaterialTheme.colorScheme.onSurfaceVariant)
            IconButton(onClick = onReview) { Ico(R.drawable.ic_review, tr("Review files")) }
        }
    }
}

// ---------------------------------------------------------------- storage browser
private data class Entry(val file: File, val size: Long?, val modified: Long = file.lastModified())

private enum class SortMode(private val en: String) {
    Biggest("Biggest first"), Oldest("Oldest first"), Newest("Newest first"), Type("File type");

    val label: String get() = tr(en)
}

private val TOP_CHOICES = listOf(0, 5, 10, 50, 100)   // 0 = all

/** Entries in the chosen order, cut to the top N (list and map both use this). */
private fun arrange(entries: List<Entry>, sort: SortMode, top: Int): List<Entry> {
    val sorted = when (sort) {
        SortMode.Biggest -> entries.sortedByDescending { it.size ?: -1 }
        SortMode.Oldest -> entries.sortedBy { it.modified }
        SortMode.Newest -> entries.sortedByDescending { it.modified }
        SortMode.Type -> entries.sortedWith(
            compareBy<Entry> { Kind.of(it.file).ordinal }.thenByDescending { it.size ?: -1 })
    }
    return if (top > 0) sorted.take(top) else sorted
}

@Composable
fun StorageScreen(onChanged: () -> Unit) {
    val ctx = LocalContext.current
    val root = remember { Scanners.root() }
    var path by remember { mutableStateOf(root) }
    var entries by remember { mutableStateOf<List<Entry>>(emptyList()) }
    var reload by remember { mutableIntStateOf(0) }
    var toDelete by remember { mutableStateOf<List<File>>(emptyList()) }   // waiting for confirmation
    var selected by remember { mutableStateOf<Set<String>>(emptySet()) }    // ticked rows / long-pressed blocks
    var preview by remember { mutableStateOf<File?>(null) }
    var error by remember { mutableStateOf<String?>(null) }
    var mapMode by remember { mutableStateOf(false) }
    var sort by remember { mutableStateOf(SortMode.Biggest) }
    var top by remember { mutableIntStateOf(0) }
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
                        }
                    }
                }
            }
        }
    }

    val total = entries.sumOf { it.size ?: 0 }
    val measured = entries.count { it.size != null }
    val measuring = measured < entries.size
    val percent = if (entries.isEmpty()) 100 else measured * 100 / entries.size
    val visible = arrange(entries, sort, top)
    val listState = androidx.compose.foundation.lazy.rememberLazyListState()
    val firstFocus = remember { FocusRequester() }
    if (BuildConfig.IS_TV) {
        // with a remote, opening a folder should put the focus on its first item, not back on the menu
        // wait until every size is in, so the list has its final order (the biggest item first)
        val settled = entries.isNotEmpty() && entries.all { it.size != null } && entries.all { it.file.parentFile == path }
        LaunchedEffect(path, mapMode, settled) {
            if (settled) {
                kotlinx.coroutines.delay(300)
                listState.scrollToItem(0)
                try { firstFocus.requestFocus() } catch (e: IllegalStateException) { /* nothing focusable yet */ }
            }
        }
    }
    val firstPath = visible.firstOrNull()?.file?.path
    LaunchedEffect(firstPath, sort, top) {
        // a re-sort put a different item first: show the top, unless the user has scrolled down the list
        if (listState.firstVisibleItemIndex <= 2) listState.scrollToItem(0)
    }
    val maxSize = (visible.maxOfOrNull { it.size ?: 0 } ?: 1).coerceAtLeast(1)

    Column(Modifier.fillMaxSize()) {
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = { path = path.parentFile ?: root }, enabled = path != root) { Ico(R.drawable.ic_up, tr("Up")) }
            Text(path.path.removePrefix(root.path).ifEmpty { "/" }.let { tr("Internal storage") + it },
                modifier = Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        Text((if (measuring) tr("Measuring {0}% ({1} of {2} items)", percent, measured, entries.size) + " · " else "") +
            tr("Total {0}", fmt(total)), style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.padding(horizontal = 16.dp, vertical = 2.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        if (measuring) {
            LinearProgressIndicator(progress = { measured.toFloat() / entries.size },
                modifier = Modifier.fillMaxWidth().padding(horizontal = 16.dp, vertical = 2.dp))
        }
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            var sortOpen by remember { mutableStateOf(false) }
            var topOpen by remember { mutableStateOf(false) }
            Box {
                TextButton(onClick = { sortOpen = true }) { Text(tr("Sort: {0}", sort.label) + " ▾") }
                DropdownMenu(expanded = sortOpen, onDismissRequest = { sortOpen = false }) {
                    SortMode.values().forEach { m ->
                        DropdownMenuItem(text = { Text(m.label) }, onClick = { sort = m; sortOpen = false })
                    }
                }
            }
            Box {
                TextButton(onClick = { topOpen = true }) { Text(tr("Show: {0}", if (top == 0) tr("All") else tr("Top {0}", top)) + " ▾") }
                DropdownMenu(expanded = topOpen, onDismissRequest = { topOpen = false }) {
                    TOP_CHOICES.forEach { n ->
                        DropdownMenuItem(text = { Text(if (n == 0) tr("All") else tr("Top {0}", n)) },
                            onClick = { top = n; topOpen = false })
                    }
                }
            }
            Spacer(Modifier.weight(1f))
            IconButton(modifier = Modifier.focusRing(), onClick = { mapMode = !mapMode }) {
                Ico(if (mapMode) R.drawable.ic_list else R.drawable.ic_map, if (mapMode) tr("List view") else tr("Map view"))
            }
        }
        HorizontalDivider()
        fun toggle(e: Entry) {
            selected = if (e.file.path in selected) selected - e.file.path else selected + e.file.path
        }
        Box(Modifier.weight(1f)) {
        if (mapMode) {
            StorageMap(visible, selected, firstFocus, onToggle = ::toggle,
                onDelete = { e -> toDelete = listOf(e.file) }) { e -> if (e.file.isDirectory) path = e.file else preview = e.file }
        } else
        LazyColumn(Modifier.fillMaxSize(), state = listState) {
            itemsIndexed(visible, key = { _, it -> it.file.path }) { i, e ->
                Column(
                    Modifier.fillMaxWidth()
                        .then(if (i == 0) Modifier.focusRequester(firstFocus) else Modifier)
                        .focusRing()
                        .clickable { if (e.file.isDirectory) path = e.file else preview = e.file }
                        .padding(horizontal = 16.dp, vertical = 6.dp)
                ) {
                    Row(verticalAlignment = Alignment.CenterVertically) {
                        Checkbox(checked = e.file.path in selected, onCheckedChange = { toggle(e) })
                        Ico(if (e.file.isDirectory) R.drawable.ic_folder else R.drawable.ic_file)
                        Spacer(Modifier.width(8.dp))
                        Text(e.file.name, modifier = Modifier.weight(1f), maxLines = 1,
                            overflow = TextOverflow.Ellipsis)
                        Text(if (e.size == null) "…" else fmt(e.size),
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                        IconButton(onClick = { toDelete = listOf(e.file) }) { Ico(R.drawable.ic_delete, tr("Delete")) }
                    }
                    LinearProgressIndicator(
                        progress = { (e.size ?: 0).toFloat() / maxSize },
                        modifier = Modifier.fillMaxWidth(),
                        trackColor = MaterialTheme.colorScheme.surfaceVariant,
                    )
                }
            }
        }
        }
        if (selected.isNotEmpty()) {
            HorizontalDivider()
            val picked = entries.filter { it.file.path in selected }
            Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp, vertical = 8.dp),
                verticalAlignment = Alignment.CenterVertically) {
                Text(tr("{0} selected · {1}", picked.size, fmt(picked.sumOf { it.size ?: 0 })),
                    modifier = Modifier.weight(1f), color = MaterialTheme.colorScheme.onSurfaceVariant)
                TextButton(onClick = { selected = emptySet() }) { Text(tr("Clear")) }
                Button(onClick = { toDelete = picked.map { it.file } }) { Text(tr("Delete selected")) }
            }
        }
    }
    LaunchedEffect(path) { selected = emptySet() }

    preview?.let { f ->
        PreviewDialog(f, { preview = null }, onDelete = { preview = null; toDelete = listOf(f) })
    }
    if (toDelete.isNotEmpty()) {
        val files = toDelete
        AlertDialog(
            onDismissRequest = { toDelete = emptyList() },
            title = { Text(if (files.size == 1) tr("Delete \"{0}\" permanently?", files[0].name) else tr("Delete {0} items permanently?", files.size)) },
            text = {
                Text(tr("Android has no trash for files, so this cannot be undone.") + "\n\n" +
                    files.take(10).joinToString("\n") { "• " + it.name } +
                    if (files.size > 10) "\n" + tr("…and {0} more", files.size - 10) else "")
            },
            confirmButton = {
                TextButton(onClick = {
                    toDelete = emptyList()
                    scope.launch {
                        val failed = withContext(Dispatchers.IO) {
                            files.filterNot { Scanners.allowed(it, ctx) && it.deleteRecursively() }
                        }
                        if (failed.isNotEmpty()) error = tr("Could not delete {0}", failed.joinToString { it.name })
                        selected = selected - files.map { it.path }.toSet()
                        reload++
                        onChanged()
                    }
                }) { Text(tr("Delete")) }
            },
            dismissButton = { TextButton(onClick = { toDelete = emptyList() }) { Text(tr("Cancel")) } },
        )
    }
    error?.let { msg ->
        AlertDialog(
            onDismissRequest = { error = null },
            text = { Text(msg) },
            confirmButton = { TextButton(onClick = { error = null }) { Text(tr("OK")) } },
        )
    }
}

/** Treemap of the current folder: each item is a block sized by its space, colored by type.
 *  Tap opens a folder or previews a file; long-press selects it for "Delete selected". */
@OptIn(ExperimentalFoundationApi::class, androidx.compose.foundation.layout.ExperimentalLayoutApi::class)
@Composable
private fun StorageMap(entries: List<Entry>, selected: Set<String>, firstFocus: FocusRequester,
                       onToggle: (Entry) -> Unit, onDelete: (Entry) -> Unit, onOpen: (Entry) -> Unit) {
    val shown = remember(entries) {
        entries.filter { (it.size ?: 0) > 0 }.sortedByDescending { it.size }.take(150)
    }
    // TV remotes have no long-press for keys in Compose: OK on a block opens a small action menu instead
    var actions by remember { mutableStateOf<Entry?>(null) }
    actions?.let { e ->
        AlertDialog(
            onDismissRequest = { actions = null },
            title = { Text(e.file.name, maxLines = 2, overflow = TextOverflow.Ellipsis) },
            text = { Text(fmt(e.size)) },
            confirmButton = {
                Row {
                    TextButton(modifier = Modifier.focusRing(), onClick = { actions = null; onOpen(e) }) {
                        Text(if (e.file.isDirectory) tr("Open") else tr("Preview"))
                    }
                    TextButton(modifier = Modifier.focusRing(), onClick = { actions = null; onToggle(e) }) {
                        Text(if (e.file.path in selected) tr("Deselect") else tr("Select"))
                    }
                    TextButton(modifier = Modifier.focusRing(), onClick = { actions = null; onDelete(e) }) {
                        Text(tr("Delete"), color = MaterialTheme.colorScheme.error)
                    }
                }
            },
            dismissButton = { TextButton(modifier = Modifier.focusRing(), onClick = { actions = null }) { Text(tr("Close")) } },
        )
    }
    Column(Modifier.fillMaxSize().padding(8.dp)) {
        BoxWithConstraints(Modifier.weight(1f).fillMaxWidth()) {
            val density = LocalDensity.current
            val wPx = with(density) { maxWidth.toPx() }
            val hPx = with(density) { maxHeight.toPx() }
            val rects = remember(shown, wPx, hPx) {
                Treemap.layout(shown.map { it.size!!.toDouble() }, wPx, hPx)
            }
            if (shown.isEmpty()) {
                Text(tr("Nothing to show"), Modifier.align(Alignment.Center),
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            shown.forEachIndexed { i, e ->
                val r = rects.getOrNull(i) ?: return@forEachIndexed
                val w = with(density) { (r.w - 2f).coerceAtLeast(0f).toDp() }
                val h = with(density) { (r.h - 2f).coerceAtLeast(0f).toDp() }
                Box(
                    Modifier
                        .offset { IntOffset((r.x + 1f).roundToInt(), (r.y + 1f).roundToInt()) }
                        .size(w, h)
                        .clip(RoundedCornerShape(3.dp))
                        .background(Kind.of(e.file).color)
                        .then(if (e.file.path in selected) Modifier.border(3.dp, Color.White, RoundedCornerShape(3.dp)) else Modifier)
                        .then(if (i == 0) Modifier.focusRequester(firstFocus) else Modifier)
                        .focusRing()
                        .combinedClickable(onClick = { if (BuildConfig.IS_TV) actions = e else onOpen(e) },
                                           onLongClick = { onToggle(e) })
                        .padding(4.dp)
                ) {
                    if (e.file.path in selected && w > 24.dp && h > 24.dp) {
                        Box(Modifier.align(Alignment.TopEnd)) { Ico(R.drawable.ic_check, tr("Selected"), Color.White, size = 16) }
                    }
                    if (w > 56.dp && h > 34.dp) {
                        Column {
                            Text(e.file.name, color = Color.White, maxLines = 1,
                                overflow = TextOverflow.Ellipsis, style = MaterialTheme.typography.labelMedium)
                            Text(fmt(e.size), color = Color.White.copy(alpha = 0.85f),
                                style = MaterialTheme.typography.labelSmall)
                        }
                    }
                }
            }
        }
        Text(if (BuildConfig.IS_TV) tr("Press OK on a block to open, select or delete it") else tr("Long-press a block to select it"),
            style = MaterialTheme.typography.labelSmall,
            color = MaterialTheme.colorScheme.onSurfaceVariant, modifier = Modifier.padding(top = 4.dp))
        // wraps onto a second line when the labels do not fit (narrow phones, longer languages)
        androidx.compose.foundation.layout.FlowRow(Modifier.fillMaxWidth().padding(top = 4.dp),
            horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Kind.values().forEach { k ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Box(Modifier.size(9.dp).clip(RoundedCornerShape(2.dp)).background(k.color))
                    Text(" " + k.label, style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onSurfaceVariant, maxLines = 1)
                }
            }
        }
    }
}

// ---------------------------------------------------------------- apps
private data class AppRow(val label: String, val pkg: String, val total: Long, val cache: Long, val data: Long,
                          val lastUsed: Long = 0)

private enum class AppSort(private val en: String) {
    Biggest("Biggest first"), LeastUsed("Least recently used"), Recent("Recently used"), Name("Name");

    val label: String get() = tr(en)
}

/** Apps in the chosen order, cut to the top N (0 = all). Never-used apps (0) count as the oldest. */
private fun arrangeApps(apps: List<AppRow>, sort: AppSort, top: Int): List<AppRow> {
    val sorted = when (sort) {
        AppSort.Biggest -> apps.sortedByDescending { it.total }
        AppSort.LeastUsed -> apps.sortedBy { it.lastUsed }
        AppSort.Recent -> apps.sortedByDescending { it.lastUsed }
        AppSort.Name -> apps.sortedBy { it.label.lowercase() }
    }
    return if (top > 0) sorted.take(top) else sorted
}

private fun lastUsedText(ts: Long): String =
    if (ts <= 0) tr("last used: unknown")
    else tr("last used {0}", DateUtils.getRelativeTimeSpanString(ts, System.currentTimeMillis(), DateUtils.DAY_IN_MILLIS)
        .toString().replaceFirstChar { it.lowercase() })

private fun hasUsageAccess(ctx: Context): Boolean = Access.hasUsageAccess(ctx)

private fun loadApps(ctx: Context): List<AppRow> {
    val pm = ctx.packageManager
    val ssm = ctx.getSystemService(StorageStatsManager::class.java)
    // last time each app was used (needs the Usage access permission the Apps tab already asks for)
    val now = System.currentTimeMillis()
    val used = try {
        ctx.getSystemService(UsageStatsManager::class.java)
            .queryAndAggregateUsageStats(now - 365L * DateUtils.DAY_IN_MILLIS, now)
    } catch (e: Exception) { emptyMap() }
    return pm.getInstalledApplications(0)
        .filter { it.flags and ApplicationInfo.FLAG_SYSTEM == 0 || it.flags and ApplicationInfo.FLAG_UPDATED_SYSTEM_APP != 0 }
        .mapNotNull { a ->
            try {
                val st = ssm.queryStatsForPackage(StorageManager.UUID_DEFAULT, a.packageName, Process.myUserHandle())
                val u = used[a.packageName]
                AppRow(pm.getApplicationLabel(a).toString(), a.packageName,
                    st.appBytes + st.dataBytes + st.cacheBytes, st.cacheBytes, st.dataBytes,
                    maxOf(u?.lastTimeUsed ?: 0L,
                        if (android.os.Build.VERSION.SDK_INT >= 29) u?.lastTimeVisible ?: 0L else 0L))
            } catch (e: Exception) { null }
        }
        .sortedByDescending { it.total }
}

@Composable
fun AppsScreen() {
    val ctx = LocalContext.current
    var access by remember { mutableStateOf(hasUsageAccess(ctx)) }
    var apps by remember { mutableStateOf<List<AppRow>?>(null) }
    var refresh by remember { mutableIntStateOf(0) }
    var walk by remember { mutableStateOf<List<AppRow>>(emptyList()) }
    var walkIndex by remember { mutableIntStateOf(-1) }
    var sort by remember { mutableStateOf(AppSort.Biggest) }
    var top by remember { mutableIntStateOf(0) }
    var uninstalled by remember { mutableStateOf(false) }   // came back from the system uninstall screen

    fun openApp(pkg: String) {
        ctx.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS, Uri.parse("package:$pkg")))
    }
    // Opens the next app in the walkthrough, or ends it
    fun advance() {
        val next = walkIndex + 1
        if (next < walk.size) { walkIndex = next; openApp(walk[next].pkg) } else walkIndex = -1
    }
    // Android's own "free up space" screen, falling back to the storage settings
    fun systemCleaner() {
        val tries = listOfNotNull(
            if (android.os.Build.VERSION.SDK_INT >= 30) Intent(StorageManager.ACTION_CLEAR_APP_CACHE) else null,
            Intent(Settings.ACTION_INTERNAL_STORAGE_SETTINGS))
        for (i in tries) {
            try { ctx.startActivity(i); return } catch (e: Exception) { /* try the next one */ }
        }
    }

    androidx.lifecycle.compose.LifecycleEventEffect(androidx.lifecycle.Lifecycle.Event.ON_RESUME) {
        access = hasUsageAccess(ctx)
        if (uninstalled) { uninstalled = false; refresh++ }
        if (walkIndex >= 0) { refresh++; advance() }   // back from an app's settings: open the next
    }
    LaunchedEffect(access, refresh) {
        apps = if (access) withContext(Dispatchers.IO) { loadApps(ctx) } else null
    }

    if (!access) {
        Column(Modifier.fillMaxSize().padding(24.dp), verticalArrangement = Arrangement.Center,
            horizontalAlignment = Alignment.CenterHorizontally) {
            Text(tr("Allow usage access"), style = MaterialTheme.typography.titleMedium)
            Text("\n" + tr("Android only shares each app's size if you allow \"Usage access\". " +
                "OpenCleaner only reads sizes. It cannot see what you do in apps.") + "\n")
            var noSettings by remember { mutableStateOf(false) }
            Button(modifier = Modifier.focusRing(), onClick = { if (!Access.openUsageSettings(ctx)) noSettings = true }) {
                Text(tr("Open settings"))
            }
            if (noSettings) {
                Text("\n" + tr("This device has no \"Usage access\" settings page (common on TVs). Grant it once from a " +
                    "computer with adb:") + "\n\n" + Access.adbCommand(ctx, "GET_USAGE_STATS") +
                    "\n\n" + tr("then come back to this tab."), style = MaterialTheme.typography.bodyMedium)
            }
        }
        return
    }
    val list = apps?.let { arrangeApps(it, sort, top) }
    if (list == null) {
        Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) { CircularProgressIndicator() }
        return
    }
    Column(Modifier.fillMaxSize()) {
        if (walkIndex >= 0 && walkIndex < walk.size) {
            Row(Modifier.fillMaxWidth().padding(16.dp, 8.dp), verticalAlignment = Alignment.CenterVertically) {
                Text(tr("Clearing caches {0} of {1}: {2}", walkIndex + 1, walk.size, walk[walkIndex].label) + "\n" +
                    tr("Tap Storage & cache, then Clear cache, then go back."),
                    style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f))
                TextButton(onClick = { advance() }) { Text(tr("Skip")) }
                TextButton(onClick = { walkIndex = -1 }) { Text(tr("Stop")) }
            }
        } else {
            val big = (apps ?: emptyList()).filter { it.cache >= 5L * 1024 * 1024 }.sortedByDescending { it.cache }.take(15)
            Row(Modifier.fillMaxWidth().padding(horizontal = 12.dp),
                horizontalArrangement = Arrangement.spacedBy(8.dp)) {
                Button(onClick = { systemCleaner() }) { Text(tr("Free up space")) }
                Button(onClick = { walk = big; walkIndex = -1; advance() }, enabled = big.isNotEmpty()) {
                    Text(tr("Clear caches ({0})", big.size))
                }
            }
        }
        Text(if (BuildConfig.IS_TV) tr("Press OK on an app to open its settings, or the bin to uninstall it. Uninstalling is permanent and removes its data; Android asks you to confirm.")
            else tr("Tap an app to open its settings, or the bin to uninstall it. Uninstalling is permanent and removes its data; Android asks you to confirm."),
            style = MaterialTheme.typography.bodySmall,
            modifier = Modifier.padding(16.dp, 8.dp),
            color = MaterialTheme.colorScheme.onSurfaceVariant)
        Row(Modifier.fillMaxWidth().padding(horizontal = 8.dp), verticalAlignment = Alignment.CenterVertically) {
            var sortOpen by remember { mutableStateOf(false) }
            var topOpen by remember { mutableStateOf(false) }
            Box {
                TextButton(onClick = { sortOpen = true }) { Text(tr("Sort: {0}", sort.label) + " ▾") }
                DropdownMenu(expanded = sortOpen, onDismissRequest = { sortOpen = false }) {
                    AppSort.values().forEach { m ->
                        DropdownMenuItem(text = { Text(m.label) }, onClick = { sort = m; sortOpen = false })
                    }
                }
            }
            Box {
                TextButton(onClick = { topOpen = true }) { Text(tr("Show: {0}", if (top == 0) tr("All") else tr("Top {0}", top)) + " ▾") }
                DropdownMenu(expanded = topOpen, onDismissRequest = { topOpen = false }) {
                    TOP_CHOICES.forEach { n ->
                        DropdownMenuItem(text = { Text(if (n == 0) tr("All") else tr("Top {0}", n)) },
                            onClick = { top = n; topOpen = false })
                    }
                }
            }
            Text(tr("{0} apps · {1}", apps?.size ?: 0, fmt(apps?.sumOf { it.total } ?: 0)),
                style = MaterialTheme.typography.bodySmall, modifier = Modifier.weight(1f).padding(start = 8.dp),
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
        HorizontalDivider()
        LazyColumn(Modifier.fillMaxSize()) {
            items(list, key = { it.pkg }) { a ->
                Row(
                    Modifier.fillMaxWidth().focusRing().clickable {
                        ctx.startActivity(Intent(Settings.ACTION_APPLICATION_DETAILS_SETTINGS,
                            Uri.parse("package:${a.pkg}")))
                    }.padding(horizontal = 16.dp, vertical = 10.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(a.label, maxLines = 1, overflow = TextOverflow.Ellipsis)
                        Text(tr("data {0} · cache {1}", fmt(a.data), fmt(a.cache)) + " · " + lastUsedText(a.lastUsed),
                            style = MaterialTheme.typography.bodySmall,
                            color = MaterialTheme.colorScheme.onSurfaceVariant)
                    }
                    Text(fmt(a.total))
                    IconButton(onClick = {
                        uninstalled = true
                        ctx.startActivity(Intent(Intent.ACTION_DELETE, Uri.parse("package:${a.pkg}")))
                    }) { Ico(R.drawable.ic_delete, tr("Uninstall {0}", a.label)) }
                }
            }
        }
    }
}

// ---------------------------------------------------------------- file preview
private val TEXT_EXT = setOf("txt", "log", "md", "json", "xml", "csv", "html", "htm", "ini", "conf",
    "cfg", "properties", "yaml", "yml", "kt", "java", "py", "js", "sh", "srt", "vcf", "gpx")

private fun mimeOf(f: File): String =
    MimeTypeMap.getSingleton().getMimeTypeFromExtension(f.extension.lowercase()) ?: "*/*"

private val VIDEO_EXT = setOf("mp4", "m4v", "3gp", "webm", "mkv", "mov")
private val AUDIO_EXT = setOf("mp3", "flac", "m4a", "aac", "wav", "ogg", "opus", "amr")

private fun clock(ms: Int): String {
    val t = (ms / 1000).coerceAtLeast(0)
    return "%d:%02d".format(t / 60, t % 60)
}

/**
 * Built-in player for video and audio: Android's VideoView with our own controls inside the dialog
 * (the stock MediaController is a floating window that covers the dialog's buttons).
 */
@Composable
private fun MediaPlayerView(f: File, audio: Boolean) {
    var video by remember { mutableStateOf<VideoView?>(null) }
    var playing by remember { mutableStateOf(false) }
    var pos by remember { mutableIntStateOf(0) }
    var dur by remember { mutableIntStateOf(0) }
    var seeking by remember { mutableStateOf(false) }

    LaunchedEffect(video) {
        val v = video ?: return@LaunchedEffect
        while (true) {
            if (!seeking) { pos = v.currentPosition; playing = v.isPlaying }
            if (dur <= 0) dur = v.duration.coerceAtLeast(0)
            delay(250)
        }
    }
    Column(Modifier.fillMaxWidth()) {
        AndroidView(
            factory = { c ->
                VideoView(c).also { v ->
                    v.setVideoPath(f.path)
                    v.setOnPreparedListener { v.seekTo(1); dur = v.duration }   // show the first frame
                    v.setOnCompletionListener { playing = false }
                    video = v
                }
            },
            // audio files still play through a VideoView, just without a picture
            modifier = Modifier.fillMaxWidth().height(if (audio) 1.dp else 320.dp),
            onRelease = { it.stopPlayback() },
        )
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onClick = {
                video?.let { v -> if (v.isPlaying) v.pause() else v.start(); playing = v.isPlaying }
            }) { Ico(if (playing) R.drawable.ic_pause else R.drawable.ic_play, if (playing) tr("Pause") else tr("Play"), size = 24) }
            Slider(
                value = pos.toFloat().coerceIn(0f, maxOf(dur, 1).toFloat()),
                onValueChange = { seeking = true; pos = it.toInt() },
                onValueChangeFinished = { video?.seekTo(pos); seeking = false },
                valueRange = 0f..maxOf(dur, 1).toFloat(),
                modifier = Modifier.weight(1f),
            )
            Text("${clock(pos)} / ${clock(dur)}", style = MaterialTheme.typography.labelSmall,
                color = MaterialTheme.colorScheme.onSurfaceVariant)
        }
    }
}

/** Image or video thumbnail, or null for other types. */
private fun loadThumb(f: File, mime: String): Bitmap? = try {
    when {
        mime.startsWith("image/") -> {
            val bounds = BitmapFactory.Options().apply { inJustDecodeBounds = true }
            BitmapFactory.decodeFile(f.path, bounds)
            var sample = 1
            while (bounds.outWidth / sample > 1600 || bounds.outHeight / sample > 1600) sample *= 2
            BitmapFactory.decodeFile(f.path, BitmapFactory.Options().apply { inSampleSize = sample })
        }
        mime.startsWith("video/") ->
            if (android.os.Build.VERSION.SDK_INT >= 29) ThumbnailUtils.createVideoThumbnail(f, Size(720, 720), null)
            else @Suppress("DEPRECATION")
                ThumbnailUtils.createVideoThumbnail(f.path, android.provider.MediaStore.Video.Thumbnails.MINI_KIND)
        else -> null
    }
} catch (e: Exception) { null }

/** First few KB of a text file, or null if it looks binary. */
private fun readHead(f: File): String? = try {
    f.inputStream().use { input ->
        val buf = ByteArray(4000)
        val n = input.read(buf)
        if (n <= 0) "" else {
            val text = String(buf, 0, n, Charsets.UTF_8)
            if (text.count { it == '\u0000' } > 0) null else text
        }
    }
} catch (e: Exception) { null }

@Composable
private fun PreviewDialog(f: File, onDismiss: () -> Unit, onDelete: (() -> Unit)? = null) {
    val ctx = LocalContext.current
    val mime = remember(f) { mimeOf(f) }
    val ext = remember(f) { f.extension.lowercase() }
    val isVideo = mime.startsWith("video/") || ext in VIDEO_EXT
    val isAudio = mime.startsWith("audio/") || ext in AUDIO_EXT
    val bitmap by produceState<Bitmap?>(null, f) {
        value = if (isVideo || isAudio) null else withContext(Dispatchers.IO) { loadThumb(f, mime) }
    }
    val text by produceState<String?>(null, f) {
        value = if (mime.startsWith("text/") || f.extension.lowercase() in TEXT_EXT)
            withContext(Dispatchers.IO) { readHead(f) } else null
    }
    val modified = remember(f) { java.text.DateFormat.getDateTimeInstance().format(java.util.Date(f.lastModified())) }

    AlertDialog(
        onDismissRequest = onDismiss,
        properties = DialogProperties(usePlatformDefaultWidth = false),
        modifier = Modifier.padding(12.dp).fillMaxWidth(),
        title = { Text(f.name, maxLines = 2, overflow = TextOverflow.Ellipsis) },
        text = {
            Column(Modifier.verticalScroll(rememberScrollState())) {
                if (isVideo || isAudio) MediaPlayerView(f, isAudio)
                bitmap?.let {
                    Image(bitmap = it.asImageBitmap(), contentDescription = null,
                        modifier = Modifier.fillMaxWidth().heightIn(max = 480.dp),
                        contentScale = ContentScale.Fit)
                }
                text?.let {
                    Text(it, style = MaterialTheme.typography.bodySmall,
                        modifier = Modifier.fillMaxWidth().heightIn(max = 320.dp)
                            .verticalScroll(rememberScrollState()))
                }
                if (bitmap == null && text == null && !isVideo && !isAudio) {
                    Text(tr("No preview for this file type. Use Open with... to view it in another app."),
                        style = MaterialTheme.typography.bodySmall)
                }
                Text("\n${fmt(f.length())} · $mime\n$modified\n${f.parent ?: ""}",
                    style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
        },
        confirmButton = {
            Row {
                if (onDelete != null) {
                    IconButton(onClick = onDelete) {
                        Ico(R.drawable.ic_delete, tr("Delete"), MaterialTheme.colorScheme.error)
                    }
                }
                TextButton(onClick = {
                    try {
                        val uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".fileprovider", f)
                        val view = Intent(Intent.ACTION_VIEW).setDataAndType(uri, mime)
                            .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
                        ctx.startActivity(Intent.createChooser(view, tr("Open with")))
                    } catch (e: Exception) { /* no app can open this file */ }
                }) { Text(tr("Open with...")) }
            }
        },
        dismissButton = { TextButton(onClick = onDismiss) { Text(tr("Close")) } },
    )
}

/** Lists what a Clean item would delete so each file can be previewed before ticking it. */
@Composable
private fun ReviewDialog(item: CleanItem, onDismiss: () -> Unit) {
    var preview by remember { mutableStateOf<File?>(null) }
    val shown = remember(item) { item.files.take(200) }
    AlertDialog(
        onDismissRequest = onDismiss,
        title = { Text(item.title, maxLines = 2, overflow = TextOverflow.Ellipsis) },
        text = {
            Column {
                Text(tr("Tap a file to preview it."), style = MaterialTheme.typography.bodySmall,
                    color = MaterialTheme.colorScheme.onSurfaceVariant)
                LazyColumn(Modifier.heightIn(max = 380.dp)) {
                    items(shown, key = { it.path }) { f ->
                        Row(
                            Modifier.fillMaxWidth().clickable(enabled = f.isFile) { preview = f }
                                .padding(vertical = 8.dp),
                            verticalAlignment = Alignment.CenterVertically,
                        ) {
                            Ico(if (f.isDirectory) R.drawable.ic_folder else R.drawable.ic_file)
                            Spacer(Modifier.width(8.dp))
                            Text(f.name, Modifier.weight(1f), maxLines = 1, overflow = TextOverflow.Ellipsis)
                            Text(if (f.isFile) fmt(f.length()) else tr("folder"),
                                style = MaterialTheme.typography.bodySmall,
                                color = MaterialTheme.colorScheme.onSurfaceVariant)
                        }
                    }
                    if (item.files.size > shown.size) {
                        item { Text(tr("…and {0} more", item.files.size - shown.size),
                            style = MaterialTheme.typography.bodySmall) }
                    }
                }
            }
        },
        confirmButton = { TextButton(onClick = onDismiss) { Text(tr("Close")) } },
    )
    preview?.let { PreviewDialog(it, { preview = null }) }
}
