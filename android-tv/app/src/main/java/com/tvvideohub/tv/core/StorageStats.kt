package com.tvvideohub.tv.core

import android.content.Context
import java.util.Locale

/** Device storage figures for the directory where the media cache lives. */
data class DeviceStorage(val freeBytes: Long, val totalBytes: Long) {
    val usedBytes: Long get() = (totalBytes - freeBytes).coerceAtLeast(0)
}

fun Context.deviceStorage(): DeviceStorage {
    val dir = filesDir
    return DeviceStorage(freeBytes = dir.usableSpace, totalBytes = dir.totalSpace)
}

/** Human-readable byte size, e.g. 1.5 GB. */
fun formatBytes(bytes: Long): String {
    if (bytes < 1024) return "$bytes B"
    val units = arrayOf("KB", "MB", "GB", "TB")
    var value = bytes.toDouble() / 1024
    var i = 0
    while (value >= 1024 && i < units.lastIndex) { value /= 1024; i++ }
    return String.format(Locale.US, "%.1f %s", value, units[i])
}
