package com.tvvideohub.tv.ui.components

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.produceState
import androidx.compose.ui.platform.LocalContext
import com.tvvideohub.tv.core.VideoThumbnails
import kotlinx.coroutines.delay
import java.io.File

/**
 * Returns the locally-generated preview frame for [id] (`filesDir/thumbs/{id}.jpg`) if one exists,
 * else null. Use this ONLY when the API gave no `thumbnailUrl`.
 *
 * It checks once synchronously (so an already-cached preview shows immediately) and then polls a
 * few times, so a frame generated in the background — on detail open or at download start —
 * appears without the user leaving the screen.
 */
@Composable
fun rememberVideoFrame(id: String): File? {
    val context = LocalContext.current
    val file by produceState<File?>(initialValue = VideoThumbnails.existing(context, id), id) {
        var tries = 0
        while (value == null && tries < 8) {
            delay(1200)
            value = VideoThumbnails.existing(context, id)
            tries++
        }
    }
    return file
}
