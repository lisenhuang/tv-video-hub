package com.tvvideohub.tv.core

import android.content.Context
import android.graphics.Bitmap
import android.media.MediaMetadataRetriever
import android.net.Uri
import android.os.Build
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Semaphore
import kotlinx.coroutines.sync.withPermit
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream

/**
 * Client-side video preview thumbnails.
 *
 * When the backend doesn't provide a `thumbnailUrl`, the app can still show a real preview by
 * extracting a frame from the video itself and caching it as a small JPEG.
 *
 * Files are keyed by the **stable video id** (never the rotating, presigned playback URL) under
 * `filesDir/thumbs/{id}.jpg`, so a generated preview survives URL rotation AND app upgrades — the
 * same rule the media cache follows (`customCacheKey`). This is purely additive on-device state:
 * it never touches the Media3 download index or the streaming cache, so it's safe across upgrades.
 */
object VideoThumbnails {
    private const val DIR = "thumbs"
    private const val FRAME_AT_US = 2_000_000L      // ~2s in: skips black intro / leader frames
    private const val MAX_W = 640
    private const val MAX_H = 360
    private const val JPEG_QUALITY = 80
    private const val MAX_CONCURRENT = 3            // cap simultaneous extractions (e.g. a list grid)

    // App-lifetime IO scope for fire-and-forget generation (e.g. at download start).
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.IO)

    // Bounds how many previews extract at once so opening a list doesn't fire a presign +
    // frame-extract job for every card simultaneously.
    private val gate = Semaphore(MAX_CONCURRENT)

    private fun dir(context: Context): File =
        File(context.applicationContext.filesDir, DIR).apply { mkdirs() }

    fun file(context: Context, id: String): File = File(dir(context), "$id.jpg")

    /** The persisted preview for [id], or null if one hasn't been generated yet. */
    fun existing(context: Context, id: String): File? =
        if (id.isBlank()) null else file(context, id).takeIf { it.exists() && it.length() > 0L }

    /**
     * Fire-and-forget generation (e.g. when a download starts, while the network is up). No-op if
     * we already have a preview, or if there's no usable source.
     */
    fun ensureAsync(context: Context, id: String, sourceUrl: String?) {
        if (id.isBlank() || sourceUrl.isNullOrBlank() || existing(context, id) != null) return
        val app = context.applicationContext
        scope.launch { runCatching { generate(app, id, sourceUrl) } }
    }

    /** Generate (if needed) and return the persisted preview, or null on failure. */
    suspend fun ensure(context: Context, id: String, sourceUrl: String?): File? {
        existing(context, id)?.let { return it }
        if (id.isBlank() || sourceUrl.isNullOrBlank()) return null
        val app = context.applicationContext
        return withContext(Dispatchers.IO) { runCatching { generate(app, id, sourceUrl) }.getOrNull() }
    }

    /**
     * Bounded generation for list screens. Returns an existing preview immediately; otherwise waits
     * for one of [MAX_CONCURRENT] slots and only THEN invokes [sourceUrlProvider] — so the
     * (network) cost of fetching a fresh playback URL is paid only when generation actually runs,
     * and never for more than a few cards at once. Safe to call from many cards concurrently.
     */
    suspend fun ensureGated(context: Context, id: String, sourceUrlProvider: suspend () -> String?): File? {
        existing(context, id)?.let { return it }
        if (id.isBlank()) return null
        return gate.withPermit {
            existing(context, id) ?: run {
                val url = runCatching { sourceUrlProvider() }.getOrNull()
                if (url.isNullOrBlank()) null
                else withContext(Dispatchers.IO) {
                    runCatching { generate(context.applicationContext, id, url) }.getOrNull()
                }
            }
        }
    }

    private fun generate(context: Context, id: String, sourceUrl: String): File? {
        val out = file(context, id)
        if (out.exists() && out.length() > 0L) return out

        val tmp = File(out.parentFile, "$id.jpg.tmp")
        val retriever = MediaMetadataRetriever()
        try {
            if (sourceUrl.startsWith("http", ignoreCase = true)) {
                // Remote progressive/mp4 — reads only the index + the requested frame's bytes.
                retriever.setDataSource(sourceUrl, HashMap<String, String>())
            } else {
                retriever.setDataSource(context, Uri.parse(sourceUrl))
            }

            val bitmap: Bitmap = (
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O_MR1) {
                    retriever.getScaledFrameAtTime(
                        FRAME_AT_US, MediaMetadataRetriever.OPTION_CLOSEST_SYNC, MAX_W, MAX_H
                    )
                } else {
                    retriever.getFrameAtTime(FRAME_AT_US, MediaMetadataRetriever.OPTION_CLOSEST_SYNC)
                } ?: retriever.frameAtTime
                ) ?: return null

            FileOutputStream(tmp).use { bitmap.compress(Bitmap.CompressFormat.JPEG, JPEG_QUALITY, it) }
            bitmap.recycle()
            return if (tmp.renameTo(out)) out else null
        } catch (t: Throwable) {
            return null
        } finally {
            runCatching { if (tmp.exists()) tmp.delete() }
            runCatching { retriever.release() }
        }
    }
}
