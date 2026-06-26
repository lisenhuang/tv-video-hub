@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.annotation.OptIn
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.aspectRatio
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.unit.dp
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.ui.draw.clip
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import coil.compose.AsyncImage
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.data.dto.VideoDetail
import com.tvvideohub.tv.download.DownloadUtil
import com.tvvideohub.tv.download.Downloads
import com.tvvideohub.tv.player.PlayerActivity
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import kotlinx.coroutines.delay

/**
 * Detail screen: shows a video and offers Play + Download/Remove (with progress) and an
 * offline badge. Reachable from the catalog; remote- and touch-friendly.
 */
@OptIn(UnstableApi::class)
class DetailActivity : ComponentActivity() {

    private val videoId: String by lazy { intent.getStringExtra(EXTRA_ID).orEmpty() }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            AppTheme { DetailScreen(videoId, onBack = { finish() }) }
        }
    }

    companion object {
        private const val EXTRA_ID = "extra_id"
        fun intent(context: Context, videoId: String): Intent =
            Intent(context, DetailActivity::class.java).putExtra(EXTRA_ID, videoId)
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun DetailScreen(videoId: String, onBack: () -> Unit) {
    val context = LocalContext.current
    val repo = remember { CatalogRepository() }
    val colors = MaterialTheme.colorScheme

    var detail by remember { mutableStateOf<VideoDetail?>(null) }
    var loadError by remember { mutableStateOf(false) }

    // Load details (online). If that fails but the video is downloaded, build an offline view.
    LaunchedEffect(videoId) {
        runCatching { repo.getVideo(videoId) }
            .onSuccess { detail = it }
            .onFailure {
                val dl = DownloadUtil.getDownload(context, videoId)
                if (dl != null) {
                    val meta = com.tvvideohub.tv.download.OfflineVideo.from(dl)
                    detail = VideoDetail(
                        id = meta.id, title = meta.title, description = "",
                        thumbnailUrl = meta.thumbnailUrl, durationSeconds = null,
                        playbackUrl = meta.uri, playbackUrlExpiresAt = "",
                        mimeType = meta.mimeType, createdAt = ""
                    )
                } else loadError = true
            }
    }

    // Poll the download state for live progress.
    val download by produceState<Download?>(initialValue = null, videoId) {
        while (true) {
            value = DownloadUtil.getDownload(context, videoId)
            delay(1000)
        }
    }

    Box(Modifier.fillMaxSize().padding(40.dp)) {
        when {
            loadError -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Column(horizontalAlignment = Alignment.CenterHorizontally) {
                    Text("Couldn't load this video.", style = MaterialTheme.typography.titleLarge, color = colors.onBackground)
                    Button(onClick = onBack, modifier = Modifier.padding(top = 16.dp)) { Text("Back") }
                }
            }
            detail == null -> Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                Text("Loading…", style = MaterialTheme.typography.titleLarge, color = colors.onBackground)
            }
            else -> {
                val d = detail!!
                Row(Modifier.fillMaxSize()) {
                    Box(
                        Modifier.weight(1f).aspectRatio(16f / 9f),
                        contentAlignment = Alignment.Center
                    ) {
                        if (d.thumbnailUrl != null) {
                            AsyncImage(
                                model = d.thumbnailUrl, contentDescription = d.title,
                                contentScale = ContentScale.Crop, modifier = Modifier.fillMaxSize()
                            )
                        } else {
                            Text("▶", style = MaterialTheme.typography.headlineLarge, color = colors.onSurface)
                        }
                    }
                    Column(Modifier.weight(1f).padding(start = 32.dp)) {
                        Text(d.title, style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)
                        if (d.description.isNotBlank()) {
                            Text(
                                d.description, style = MaterialTheme.typography.bodyMedium,
                                color = colors.onSurface, modifier = Modifier.padding(top = 12.dp)
                            )
                        }
                        DownloadProgress(download, modifier = Modifier.padding(top = 16.dp))
                        Row(
                            modifier = Modifier.padding(top = 24.dp),
                            horizontalArrangement = Arrangement.spacedBy(16.dp)
                        ) {
                            Button(onClick = { startPlayback(context, d, download) }) { Text("Play") }
                            DownloadButton(context, d, download)
                            Button(onClick = onBack) { Text("Back") }
                        }
                    }
                }
            }
        }
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun DownloadProgress(download: Download?, modifier: Modifier = Modifier) {
    val colors = MaterialTheme.colorScheme
    when (download?.state) {
        Download.STATE_DOWNLOADING, Download.STATE_QUEUED -> Column(modifier) {
            val pct = download.percentDownloaded.let { if (it.isNaN()) 0f else it }
            Text("Downloading ${pct.toInt()}%", style = MaterialTheme.typography.labelMedium, color = colors.onSurface)
            Box(
                Modifier
                    .fillMaxWidth()
                    .padding(top = 6.dp)
                    .height(6.dp)
                    .clip(RoundedCornerShape(3.dp))
                    .background(colors.surfaceVariant)
            ) {
                Box(
                    Modifier
                        .fillMaxWidth((pct / 100f).coerceIn(0f, 1f))
                        .height(6.dp)
                        .clip(RoundedCornerShape(3.dp))
                        .background(colors.primary)
                )
            }
        }
        Download.STATE_COMPLETED ->
            Text("✓ Available offline", style = MaterialTheme.typography.labelMedium, color = colors.primary, modifier = modifier)
        Download.STATE_FAILED ->
            Text("Download failed", style = MaterialTheme.typography.labelMedium, color = colors.onSurface, modifier = modifier)
        else -> {}
    }
}

@OptIn(UnstableApi::class)
@Composable
private fun DownloadButton(context: Context, detail: VideoDetail, download: Download?) {
    when (download?.state) {
        null, Download.STATE_FAILED, Download.STATE_REMOVING ->
            Button(onClick = { Downloads.start(context, detail) }) { Text("Download") }
        Download.STATE_DOWNLOADING, Download.STATE_QUEUED ->
            Button(onClick = { Downloads.remove(context, detail.id) }) { Text("Cancel") }
        Download.STATE_COMPLETED ->
            Button(onClick = { Downloads.remove(context, detail.id) }) { Text("Remove download") }
        else ->
            Button(onClick = { Downloads.start(context, detail) }) { Text("Download") }
    }
}

@OptIn(UnstableApi::class)
private fun startPlayback(context: Context, detail: VideoDetail, download: Download?) {
    val intent = if (download?.state == Download.STATE_COMPLETED) {
        PlayerActivity.offlineIntent(context, detail.id, download.request.uri.toString(), detail.mimeType)
    } else {
        PlayerActivity.intent(context, detail.id)
    }
    context.startActivity(intent)
}
