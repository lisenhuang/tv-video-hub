package com.tvvideohub.tv.ui

import android.content.Intent
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
import androidx.compose.foundation.lazy.grid.rememberLazyGridState
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.layout.ContentScale
import androidx.compose.ui.platform.LocalConfiguration
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.ui.unit.dp
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel
import androidx.media3.common.util.UnstableApi
import androidx.tv.material3.Border
import androidx.tv.material3.Button
import androidx.tv.material3.Card
import androidx.tv.material3.CardDefaults
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import coil.compose.AsyncImage
import com.tvvideohub.tv.R
import com.tvvideohub.tv.data.dto.VideoSummary
import com.tvvideohub.tv.download.DownloadUtil

/**
 * Catalog route: hosts [CatalogViewModel] and renders the browse grid plus a header with
 * Downloads / Settings actions. Cards show an "Offline" badge when downloaded. Works with
 * both D-pad/remote (focusable cards, initial focus) and touch (Card.onClick fires on tap).
 */
@OptIn(UnstableApi::class)
@Composable
fun CatalogRoute(
    onVideoSelected: (String) -> Unit,
    onOpenDownloads: () -> Unit,
    onOpenSettings: () -> Unit,
    onOpenInstallSettings: (Intent) -> Unit,
) {
    val context = LocalContext.current
    val vm: CatalogViewModel = viewModel(
        factory = ViewModelProvider.AndroidViewModelFactory.getInstance(
            context.applicationContext as android.app.Application
        )
    )
    val uiState by vm.uiState.collectAsStateWithLifecycle()
    val updateState by vm.updateState.collectAsStateWithLifecycle()

    // Snapshot of completed-download ids, for the offline badge.
    val downloadedIds by produceState(initialValue = emptySet<String>(), uiState) {
        value = DownloadUtil.listDownloads(context)
            .filter { it.state == androidx.media3.exoplayer.offline.Download.STATE_COMPLETED }
            .map { it.request.id }
            .toSet()
    }

    Box(modifier = Modifier.fillMaxSize().padding(horizontal = 32.dp, vertical = 24.dp)) {
        Column(Modifier.fillMaxSize()) {
            CatalogHeader(onOpenDownloads = onOpenDownloads, onOpenSettings = onOpenSettings)

            Box(Modifier.fillMaxSize()) {
                when (val state = uiState) {
                    is CatalogUiState.Loading -> CenteredMessage(stringResource(R.string.catalog_loading))
                    is CatalogUiState.Empty -> CenteredMessage(stringResource(R.string.catalog_empty))
                    is CatalogUiState.Error -> ErrorState(state.message, onRetry = vm::loadVideos)
                    is CatalogUiState.Content -> VideoGrid(
                        videos = state.videos,
                        downloadedIds = downloadedIds,
                        onVideoSelected = onVideoSelected
                    )
                }
            }
        }

        UpdateOverlay(
            updateState = updateState,
            onConfirm = vm::startUpdate,
            onDismiss = vm::dismissUpdate,
            onOpenSettings = { vm.dismissUpdate(); onOpenInstallSettings(vm.unknownSourcesSettingsIntent()) }
        )
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun CatalogHeader(onOpenDownloads: () -> Unit, onOpenSettings: () -> Unit) {
    Row(
        modifier = Modifier.fillMaxWidth().padding(bottom = 16.dp),
        verticalAlignment = Alignment.CenterVertically
    ) {
        Text(
            text = stringResource(R.string.catalog_title),
            style = MaterialTheme.typography.headlineMedium,
            color = MaterialTheme.colorScheme.onBackground,
            modifier = Modifier.weight(1f)
        )
        Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
            Button(onClick = onOpenDownloads) { Text(stringResource(R.string.action_open_downloads)) }
            Button(onClick = onOpenSettings) { Text(stringResource(R.string.action_open_settings)) }
        }
    }
}

@Composable
private fun VideoGrid(
    videos: List<VideoSummary>,
    downloadedIds: Set<String>,
    onVideoSelected: (String) -> Unit
) {
    // Responsive columns: a wide TV gets ~6 columns, a narrow phone ~2.
    val widthDp = LocalConfiguration.current.screenWidthDp
    val columns = (widthDp / 220).coerceIn(2, 6)

    val gridState = rememberLazyGridState()
    val firstItemFocus = remember { FocusRequester() }

    LaunchedEffect(videos.isNotEmpty()) {
        if (videos.isNotEmpty()) runCatching { firstItemFocus.requestFocus() }
    }

    LazyVerticalGrid(
        columns = GridCells.Fixed(columns),
        state = gridState,
        contentPadding = PaddingValues(4.dp),
        horizontalArrangement = Arrangement.spacedBy(16.dp),
        verticalArrangement = Arrangement.spacedBy(16.dp),
        modifier = Modifier.fillMaxSize()
    ) {
        items(items = videos, key = { it.id }) { video ->
            val isFirst = video.id == videos.first().id
            VideoCard(
                video = video,
                isDownloaded = video.id in downloadedIds,
                onClick = { onVideoSelected(video.id) },
                modifier = if (isFirst) Modifier.focusRequester(firstItemFocus) else Modifier
            )
        }
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun VideoCard(
    video: VideoSummary,
    isDownloaded: Boolean,
    onClick: () -> Unit,
    modifier: Modifier = Modifier
) {
    Card(
        onClick = onClick,
        modifier = modifier.fillMaxWidth(),
        scale = CardDefaults.scale(focusedScale = 1.08f),
        border = CardDefaults.border(
            focusedBorder = Border(BorderStroke(3.dp, MaterialTheme.colorScheme.primary))
        )
    ) {
        Column {
            Box(Modifier.fillMaxWidth().aspectRatio(16f / 9f)) {
                if (video.thumbnailUrl != null) {
                    AsyncImage(
                        model = video.thumbnailUrl,
                        contentDescription = video.title,
                        contentScale = ContentScale.Crop,
                        modifier = Modifier.fillMaxSize()
                    )
                } else {
                    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
                        Text("▶", color = Color(0xFF8893A7), style = MaterialTheme.typography.headlineLarge)
                    }
                }
                if (isDownloaded) {
                    Text(
                        text = stringResource(R.string.badge_offline),
                        style = MaterialTheme.typography.labelSmall,
                        color = MaterialTheme.colorScheme.onPrimary,
                        modifier = Modifier
                            .align(Alignment.TopEnd)
                            .padding(6.dp)
                    )
                }
            }
            Text(
                text = video.title,
                style = MaterialTheme.typography.titleSmall,
                color = MaterialTheme.colorScheme.onSurface,
                maxLines = 2,
                overflow = TextOverflow.Ellipsis,
                modifier = Modifier.padding(horizontal = 12.dp, vertical = 8.dp)
            )
        }
    }
}

@Composable
private fun CenteredMessage(text: String) {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(text, style = MaterialTheme.typography.titleLarge, color = MaterialTheme.colorScheme.onBackground)
    }
}
