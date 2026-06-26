package com.tvvideohub.tv.download

import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import com.tvvideohub.tv.data.dto.VideoSummary
import kotlinx.serialization.Serializable

/**
 * Metadata stashed in [androidx.media3.exoplayer.offline.DownloadRequest.data] so a
 * downloaded video can be listed and played fully offline (title/thumbnail/mime) without
 * needing the backend catalog. Keep this additive/back-compatible: only add new optional
 * fields, never remove or repurpose existing ones, or older downloads stop decoding
 * after an app upgrade (see CLAUDE.md).
 */
@Serializable
data class DownloadMetadata(
    val id: String,
    val title: String,
    val thumbnailUrl: String? = null,
    val mimeType: String = "video/mp4",
    val uri: String = ""
)

/** A downloaded (or downloading) item, projected for the UI. */
@UnstableApi
data class OfflineVideo(
    val id: String,
    val title: String,
    val thumbnailUrl: String?,
    val mimeType: String,
    val uri: String,
    val state: Int,            // Download.STATE_*
    val percentDownloaded: Float,
    val bytesDownloaded: Long
) {
    val isComplete: Boolean get() = state == Download.STATE_COMPLETED
    val isFailed: Boolean get() = state == Download.STATE_FAILED

    fun toSummary(): VideoSummary =
        VideoSummary(id = id, title = title, thumbnailUrl = thumbnailUrl, createdAt = "")

    companion object {
        @UnstableApi
        fun from(download: Download): OfflineVideo {
            val meta = runCatching {
                DownloadUtil.json.decodeFromString(
                    DownloadMetadata.serializer(),
                    String(download.request.data)
                )
            }.getOrNull()
            return OfflineVideo(
                id = download.request.id,
                title = meta?.title ?: download.request.id,
                thumbnailUrl = meta?.thumbnailUrl,
                mimeType = meta?.mimeType ?: download.request.mimeType ?: "video/mp4",
                uri = download.request.uri.toString(),
                state = download.state,
                percentDownloaded = download.percentDownloaded.let { if (it.isNaN()) 0f else it },
                bytesDownloaded = download.bytesDownloaded
            )
        }
    }
}
