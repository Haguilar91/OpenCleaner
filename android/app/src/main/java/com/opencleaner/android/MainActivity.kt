package com.opencleaner.android

import android.os.Bundle
import android.os.Environment
import androidx.activity.ComponentActivity
import androidx.compose.material3.AlertDialog
import androidx.compose.material3.IconButton
import androidx.compose.material3.TextButton
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.ui.Alignment
import androidx.compose.ui.platform.LocalContext
import androidx.compose.foundation.layout.heightIn
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.verticalScroll
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.safeDrawingPadding
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.NavigationBar
import androidx.compose.material3.NavigationBarItem
import androidx.compose.material3.Scaffold
import androidx.compose.material3.Surface
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableIntStateOf
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.unit.dp
import androidx.lifecycle.Lifecycle
import androidx.lifecycle.compose.LifecycleEventEffect
import androidx.lifecycle.viewmodel.compose.viewModel

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            OpenCleanerTheme {
                Surface(Modifier.fillMaxSize().safeDrawingPadding()) { AppRoot() }
            }
        }
    }
}

@Composable
fun AppRoot() {
    var granted by remember { mutableStateOf(Environment.isExternalStorageManager()) }
    LifecycleEventEffect(Lifecycle.Event.ON_RESUME) { granted = Environment.isExternalStorageManager() }

    if (!granted) {
        PermissionScreen()
        return
    }

    val vm: CleanVm = viewModel()
    var tab by remember { mutableIntStateOf(0) }
    val tabs = listOf(R.drawable.ic_clean to "Clean", R.drawable.ic_storage to "Storage", R.drawable.ic_apps to "Apps")

    Scaffold(
        bottomBar = {
            NavigationBar {
                tabs.forEachIndexed { i, (icon, label) ->
                    NavigationBarItem(
                        selected = tab == i,
                        onClick = { tab = i },
                        icon = { Ico(icon) },
                        label = { Text(label) },
                    )
                }
            }
        },
    ) { pad ->
        Column(Modifier.padding(pad)) {
            Row(Modifier.fillMaxWidth().padding(16.dp, 12.dp), verticalAlignment = Alignment.CenterVertically) {
                Text("OpenCleaner", style = MaterialTheme.typography.titleLarge,
                    modifier = Modifier.weight(1f))
                UpdateButton()
                Text(vm.free, color = MaterialTheme.colorScheme.onSurfaceVariant)
            }
            when (tab) {
                0 -> CleanScreen(vm)
                1 -> StorageScreen(onChanged = { vm.refreshFree() })
                else -> AppsScreen()
            }
        }
    }
}

/** Checks GitHub on start; shows "Update to x" when a newer release exists, or a check button otherwise. */
@Composable
fun UpdateButton() {
    val ctx = LocalContext.current
    val scope = rememberCoroutineScope()
    val current = remember { Updater.currentVersion(ctx) }
    var release by remember { mutableStateOf<Release?>(null) }
    var ask by remember { mutableStateOf(false) }
    var busy by remember { mutableStateOf(false) }
    var message by remember { mutableStateOf<String?>(null) }

    fun check(manual: Boolean) = scope.launch {
        try {
            val r = withContext(Dispatchers.IO) { Updater.latest() }
            if (Updater.isNewer(r.version, current)) { release = r; if (manual) ask = true }
            else if (manual) message = "OpenCleaner $current is up to date."
        } catch (e: Exception) {
            if (manual) message = "Could not check for updates: ${e.message}"
        }
    }
    LaunchedEffect(Unit) { check(false) }

    val r = release
    if (r != null) {
        TextButton(onClick = { ask = true }, enabled = !busy) { Text(if (busy) "Updating…" else "Update to ${r.version}") }
    } else {
        IconButton(onClick = { check(true) }) { Ico(R.drawable.ic_update, "Check for updates ($current)") }
    }
    if (ask && r != null) {
        AlertDialog(
            onDismissRequest = { ask = false },
            title = { Text("Update to OpenCleaner ${r.version}?") },
            text = {
                Text("You have $current. The update is downloaded from GitHub and checked against the release " +
                    "checksums; Android then asks you to confirm the install.\n\n" + r.notes.take(500),
                    modifier = Modifier.heightIn(max = 360.dp).verticalScroll(rememberScrollState()))
            },
            confirmButton = {
                TextButton(onClick = {
                    ask = false; busy = true
                    scope.launch {
                        try {
                            val apk = withContext(Dispatchers.IO) { Updater.download(ctx, r) }
                            Updater.install(ctx, apk)
                        } catch (e: Exception) {
                            message = "Update failed: ${e.message}\n\n${r.page}"
                        }
                        busy = false
                    }
                }) { Text("Update") }
            },
            dismissButton = { TextButton(onClick = { ask = false }) { Text("Later") } },
        )
    }
    message?.let { m ->
        AlertDialog(onDismissRequest = { message = null }, text = { Text(m) },
            confirmButton = { TextButton(onClick = { message = null }) { Text("OK") } })
    }
}
