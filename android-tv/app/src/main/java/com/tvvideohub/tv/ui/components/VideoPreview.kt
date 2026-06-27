package com.tvvideohub.tv.ui.components

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.runtime.remember
import androidx.compose.ui.platform.LocalContext
import com.tvvideohub.tv.core.VideoThumbnails
import com.tvvideohub.tv.data.CatalogRepository
import java.io.File

/**
 * Returns a locally-generated preview frame for [id] (`filesDir/thumbs/{id}.jpg`). Use this ONLY
 * when the API gave no `thumbnailUrl`.
 *
 * If a preview already exists it's returned immediately. Otherwise this **generates one on the
 * spot** — it fetches a fresh playback URL for [id] and extracts a frame — so previews appear right
 * in the list/grid, not only after opening a video. Generation is bounded
 * ([VideoThumbnails.ensureGated]) so a grid of cards doesn't fire dozens of jobs at once, and the
 * result is cached by stable id, so it's a one-time cost per video. produceState is keyed by [id],
 * so a recycled card regenerates for its new video and cancels work for cards scrolled away.
 */
@Composable
fun rememberVideoFrame(id: String): File? {
    val context = LocalContext.current
    val repo = remember { CatalogRepository() }
    val file by produceState<File?>(initialValue = VideoThumbnails.existing(context, id), id) {
        if (value == null && id.isNotBlank()) {
            value = VideoThumbnails.ensureGated(context, id) {
                runCatching { repo.getVideo(id).playbackUrl }.getOrNull()
            }
        }
    }
    return file
}
