@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

import com.tvvideohub.tv.ui.components.rememberVideoFrame
import com.tvvideohub.tv.ui.components.tapClickable
import android.content.Context
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.annotation.OptIn
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.lazy.grid.GridCells
import androidx.compose.foundation.lazy.grid.LazyVerticalGrid
import androidx.compose.foundation.lazy.grid.items
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import coil.compose.AsyncImage
import com.tvvideohub.tv.R
import com.tvvideohub.tv.core.LocaleHelper
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.download.DownloadUtil
import com.tvvideohub.tv.download.Downloads
import com.tvvideohub.tv.download.OfflineVideo
import com.tvvideohub.tv.player.PlayerActivity
import androidx.tv.material3.Border
import androidx.tv.material3.Card
import androidx.tv.material3.CardDefaults
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import kotlinx.coroutines.delay

/**
 * Lists downloaded (and in-progress) videos. Fully usable offline: it reads the local
 * Media3 download index and plays completed items straight from the cache.
 */
@OptIn(UnstableApi::class)
class DownloadsActivity : ComponentActivity() {
    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, SettingsStore.get(newBase).language.value))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { AppTheme { DownloadsScreen() } }
    }
}

@OptIn(UnstableApi::class, ExperimentalTvMaterial3Api::class)
@Composable
private fun DownloadsScreen() {
    val context = LocalContext.current
    val colors = MaterialTheme.colorScheme

    // Poll so progress and new completions show without a manual refresh.
    val items by produceState(initialValue = emptyList<OfflineVideo>()) {
        while (true) {
            value = DownloadUtil.listDownloads(context).map { OfflineVideo.from(it) }
            delay(1000)
        }
    }

    Box(Modifier.fillMaxSize().padding(horizontal = 32.dp, vertical = 24.dp)) {
        Column(Modifier.fillMaxSize()) {
            Text(
                stringResource(R.string.downloads_title),
                style = MaterialTheme.typography.headlineMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(bottom = 16.dp)
            )
            if (items.isEmpty()) {
                Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                    Text(stringResource(R.string.downloads_empty), style = MaterialTheme.typography.titleLarge, color = colors.onBackground)
                }
            } else {
                val columns = (LocalConfiguration.current.screenWidthDp / 220).coerceIn(2, 6)
                LazyVerticalGrid(
                    columns = GridCells.Fixed(columns),
                    contentPadding = PaddingValues(4.dp),
                    horizontalArrangement = Arrangement.spacedBy(16.dp),
                    verticalArrangement = Arrangement.spacedBy(16.dp),
                    modifier = Modifier.fillMaxSize()
                ) {
                    items(items = items, key = { it.id }) { item ->
                        OfflineCard(
                            item = item,
                            onPlay = {
                                if (item.isComplete) {
                                    context.startActivity(
                                        PlayerActivity.offlineIntent(context, item.id, item.uri, item.mimeType)
                                    )
                                }
                            },
                            onRemove = { Downloads.remove(context, item.id) }
                        )
                    }
                }
            }
        }
    }
}

@OptIn(UnstableApi::class, ExperimentalTvMaterial3Api::class)
@Composable
private fun OfflineCard(item: OfflineVideo, onPlay: () -> Unit, onRemove: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Card(
        onClick = { if (item.isComplete) onPlay() else onRemove() },
        modifier = Modifier.fillMaxWidth().tapClickable { if (item.isComplete) onPlay() else onRemove() },
        scale = CardDefaults.scale(focusedScale = 1.08f),
        border = CardDefaults.border(focusedBorder = Border(BorderStroke(3.dp, colors.primary)))
    ) {
        Column {
            Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f), contentAlignment = Alignment.Center) {
                // No API thumbnail? show a frame extracted from the video (generated at download
                // start, while the network was up), so downloaded items get a real offline preview.
                val frame = if (item.thumbnailUrl == null) rememberVideoFrame(item.id) else null
                when {
                    item.thumbnailUrl != null -> AsyncImage(
                        model = item.thumbnailUrl, contentDescription = item.title,
                        contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()
                    )
                    frame != null -> AsyncImage(
                        model = frame, contentDescription = item.title,
                        contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()
                    )
                    else -> Text("▶", style = MaterialTheme.typography.headlineLarge, color = colors.onSurface)
                }
            }
            val status = when (item.state) {
                Download.STATE_COMPLETED -> stringResource(R.string.download_status_completed)
                Download.STATE_DOWNLOADING, Download.STATE_QUEUED ->
                    stringResource(R.string.download_status_downloading, item.percentDownloaded.toInt())
                Download.STATE_FAILED -> stringResource(R.string.download_status_failed)
                else -> stringResource(R.string.download_status_pending)
            }
            Text(
                text = item.title,
                style = MaterialTheme.typography.titleSmall,
                color = colors.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(start = 12.dp, end = 12.dp, top = 8.dp)
            )
            Text(
                text = status,
                style = MaterialTheme.typography.labelSmall,
                color = colors.onSurface,
                maxLines = 1,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(start = 12.dp, end = 12.dp, top = 2.dp, bottom = 8.dp)
            )
        }
    }
}
