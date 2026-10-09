package com.opencleaner.android

import android.app.Application
import android.os.Environment
import android.os.StatFs
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

class CleanVm(app: Application) : AndroidViewModel(app) {
    var items by mutableStateOf<List<CleanItem>>(emptyList()); private set
    var selected by mutableStateOf<Set<String>>(emptySet()); private set
    var busy by mutableStateOf(false); private set
    var busyText by mutableStateOf("Scanning…"); private set
    var message by mutableStateOf<String?>(null)
    var confirming by mutableStateOf(false)
    var free by mutableStateOf(""); private set

    val chosen: List<CleanItem> get() = items.filter { it.id in selected && !it.empty }

    init {
        refreshFree()
        scan()
    }

    private fun freeBytes(): Long = StatFs(Environment.getDataDirectory().path).availableBytes

    fun refreshFree() {
        val s = StatFs(Environment.getDataDirectory().path)
        val usedPct = 100.0 * (s.totalBytes - s.availableBytes) / s.totalBytes
        free = "${fmt(s.availableBytes)} free · ${usedPct.toInt()}% used"
    }

    fun toggle(id: String) {
        selected = if (id in selected) selected - id else selected + id
    }

    fun scan() {
        if (busy) return
        busy = true
        busyText = "Scanning…"
        viewModelScope.launch {
            val found = withContext(Dispatchers.IO) { Scanners.runAll(getApplication()) }
            items = found
            selected = found.filter { it.selected && !it.empty }.map { it.id }.toSet()
            busy = false
            refreshFree()
        }
    }

    fun clean() {
        val todo = chosen
        if (todo.isEmpty() || busy) return
        busy = true
        busyText = "Cleaning…"
        val before = freeBytes()
        viewModelScope.launch {
            val results = withContext(Dispatchers.IO) { todo.map { Scanners.clean(getApplication(), it) } }
            busy = false
            refreshFree()
            val freed = freeBytes() - before
            val sb = StringBuilder("Cleaning finished")
            if (freed > 0) sb.append(" · freed ${fmt(freed)}")
            val skipped = results.sumOf { it.skipped }
            if (skipped > 0) sb.append("\n\n$skipped item(s) could not be deleted.")
            results.filter { it.error != null }.forEach { sb.append("\n\n${it.title}: ${it.error}") }
            message = sb.toString()
            scan()
        }
    }
}
