@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.annotation.OptIn
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import com.tvvideohub.tv.core.deviceStorage
import com.tvvideohub.tv.core.formatBytes
import com.tvvideohub.tv.download.DownloadUtil
import com.tvvideohub.tv.download.Downloads
import com.tvvideohub.tv.download.OfflineVideo
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import kotlinx.coroutines.delay

/**
 * Storage manager: shows how much space the app's media cache uses vs. the device, and lets
 * the user delete cached/downloaded videos to free space.
 */
@OptIn(UnstableApi::class)
class StorageActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { AppTheme { StorageScreen() } }
    }
}

private data class CachedItem(
    val key: String,
    val title: String,
    val sizeBytes: Long,
    val isDownload: Boolean,
)

@OptIn(UnstableApi::class)
@Composable
private fun StorageScreen() {
    val context = LocalContext.current
    val colors = MaterialTheme.colorScheme

    val snapshot by produceState(initialValue = Triple(0L, 0L to 0L, emptyList<CachedItem>())) {
        while (true) {
            val downloads = DownloadUtil.listDownloads(context).map { OfflineVideo.from(it) }
            val titleById = downloads.associate { it.id to it.title }
            val downloadIds = downloads.map { it.id }.toSet()
            val items = DownloadUtil.cachedKeys(context).map { key ->
                CachedItem(
                    key = key,
                    title = titleById[key] ?: "Video ${key.take(8)}",
                    sizeBytes = DownloadUtil.cachedBytesFor(context, key),
                    isDownload = key in downloadIds,
                )
            }.sortedByDescending { it.sizeBytes }
            val device = context.deviceStorage()
            value = Triple(
                DownloadUtil.cacheUsedBytes(context),
                device.freeBytes to device.totalBytes,
                items
            )
            delay(1500)
        }
    }
    val (cacheUsed, device, items) = snapshot
    val (freeBytes, totalBytes) = device

    Box(Modifier.fillMaxSize().padding(horizontal = 32.dp, vertical = 24.dp)) {
        Column(Modifier.fillMaxSize()) {
            Text("Storage", style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)

            // Summary
            Column(
                modifier = Modifier
                    .padding(top = 16.dp)
                    .fillMaxWidth()
                    .clip(RoundedCornerShape(12.dp))
                    .background(colors.surfaceVariant)
                    .padding(16.dp)
            ) {
                Text(
                    "This app's cached videos: ${formatBytes(cacheUsed)}",
                    style = MaterialTheme.typography.titleMedium, color = colors.onSurface
                )
                val usedDevice = (totalBytes - freeBytes).coerceAtLeast(0)
                Text(
                    "Device storage: ${formatBytes(freeBytes)} free of ${formatBytes(totalBytes)}",
                    style = MaterialTheme.typography.bodyMedium, color = colors.onSurface,
                    modifier = Modifier.padding(top = 4.dp)
                )
                // Usage bar (device used).
                val frac = if (totalBytes > 0) (usedDevice.toFloat() / totalBytes).coerceIn(0f, 1f) else 0f
                Box(
                    Modifier.fillMaxWidth().padding(top = 10.dp).height(8.dp)
                        .clip(RoundedCornerShape(4.dp)).background(colors.background)
                ) {
                    Box(Modifier.fillMaxWidth(frac).height(8.dp).clip(RoundedCornerShape(4.dp)).background(colors.primary))
                }
            }

            Row(modifier = Modifier.padding(top = 16.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(onClick = {
                    // Clear streamed (non-download) cache; downloads stay (manage in Downloads).
                    items.filter { !it.isDownload }.forEach { DownloadUtil.removeFromCache(context, it.key) }
                }) { Text("Clear streaming cache") }
            }

            if (items.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text("Nothing cached yet.", style = MaterialTheme.typography.titleMedium, color = colors.onBackground)
                }
            } else {
                LazyColumn(
                    modifier = Modifier.fillMaxSize().padding(top = 16.dp),
                    verticalArrangement = Arrangement.spacedBy(8.dp)
                ) {
                    items(items = items, key = { it.key }) { item ->
                        StorageRow(
                            item = item,
                            onDelete = {
                                if (item.isDownload) Downloads.remove(context, item.key)
                                else DownloadUtil.removeFromCache(context, item.key)
                            }
                        )
                    }
                }
            }
        }
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun StorageRow(item: CachedItem, onDelete: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .clip(RoundedCornerShape(10.dp))
            .background(colors.surface)
            .padding(horizontal = 16.dp, vertical = 12.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Column(Modifier.weight(1f)) {
            Text(
                item.title, style = MaterialTheme.typography.titleSmall, color = colors.onSurface,
                maxLines = 1, overflow = TextOverflow.Ellipsis
            )
            Text(
                "${formatBytes(item.sizeBytes)} • ${if (item.isDownload) "downloaded (offline)" else "streaming cache"}",
                style = MaterialTheme.typography.labelSmall, color = colors.onSurface
            )
        }
        Button(onClick = onDelete) { Text("Delete") }
    }
}
