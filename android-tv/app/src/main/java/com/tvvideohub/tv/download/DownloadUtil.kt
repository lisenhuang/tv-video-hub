package com.tvvideohub.tv.download

import android.content.Context
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.PriorityTaskManager
import androidx.media3.common.util.UnstableApi
import androidx.media3.database.DatabaseProvider
import androidx.media3.database.StandaloneDatabaseProvider
import androidx.media3.datasource.DataSource
import androidx.media3.datasource.DataSpec
import androidx.media3.datasource.DefaultDataSource
import androidx.media3.datasource.DefaultHttpDataSource
import androidx.media3.datasource.TransferListener
import androidx.media3.datasource.cache.CacheDataSource
import androidx.media3.datasource.cache.CacheWriter
import androidx.media3.datasource.cache.LeastRecentlyUsedCacheEvictor
import androidx.media3.datasource.cache.SimpleCache
import androidx.media3.exoplayer.offline.Download
import androidx.media3.exoplayer.offline.DownloadManager
import androidx.media3.exoplayer.offline.DownloadNotificationHelper
import androidx.media3.exoplayer.offline.DownloadRequest
import com.tvvideohub.tv.data.dto.VideoDetail
import kotlinx.serialization.json.Json
import java.io.File
import java.io.InterruptedIOException
import java.util.concurrent.Executors

/**
 * Process-wide singletons for media caching and offline downloads.
 *
 * ONE [SimpleCache] is shared by both playback (cache-while-streaming) and the
 * [DownloadManager] (full offline downloads), so a partially-watched video and an
 * explicit download fill the same cache. Everything is keyed by the **stable video id**
 * ([DownloadRequest.customCacheKey] / [MediaItem]'s `customCacheKey`), NOT by the
 * playback URL — the backend hands out short-lived presigned URLs that rotate, so
 * URL-based keys would orphan cached bytes on every refresh and on every app update.
 *
 * Backward-compat (see CLAUDE.md): the cache directory name, the database provider, and
 * the cache-key scheme are part of the on-device contract. Do not rename/relocate them
 * or existing cached/downloaded videos would be lost on upgrade.
 */
@UnstableApi
object DownloadUtil {

    private const val CACHE_DIR = "media_cache"
    private const val MAX_CACHE_BYTES = 8L * 1024 * 1024 * 1024 // 8 GB LRU budget
    private const val MAX_PARALLEL_DOWNLOADS = 2

    const val DOWNLOAD_NOTIFICATION_CHANNEL_ID = "downloads"

    val json: Json = Json { ignoreUnknownKeys = true; explicitNulls = false }

    private val lock = Any()

    @Volatile private var databaseProvider: DatabaseProvider? = null
    @Volatile private var simpleCache: SimpleCache? = null
    @Volatile private var httpDataSourceFactory: DefaultHttpDataSource.Factory? = null
    @Volatile private var downloadManager: DownloadManager? = null
    @Volatile private var notificationHelper: DownloadNotificationHelper? = null
    @Volatile private var priorityTaskManager: PriorityTaskManager? = null

    fun getDatabaseProvider(context: Context): DatabaseProvider =
        databaseProvider ?: synchronized(lock) {
            databaseProvider ?: StandaloneDatabaseProvider(context.applicationContext)
                .also { databaseProvider = it }
        }

    fun getCache(context: Context): SimpleCache =
        simpleCache ?: synchronized(lock) {
            simpleCache ?: SimpleCache(
                File(context.applicationContext.filesDir, CACHE_DIR),
                LeastRecentlyUsedCacheEvictor(MAX_CACHE_BYTES),
                getDatabaseProvider(context)
            ).also { simpleCache = it }
        }

    private fun getHttpDataSourceFactory(): DefaultHttpDataSource.Factory =
        httpDataSourceFactory ?: synchronized(lock) {
            httpDataSourceFactory ?: DefaultHttpDataSource.Factory()
                .setAllowCrossProtocolRedirects(true)
                .also { httpDataSourceFactory = it }
        }

    /**
     * Cache-backed data source factory used by BOTH the player and downloads. Reads come
     * from the cache when present; misses fall through to HTTP and are written back. When
     * fully cached (a completed download), playback never touches the network — that's
     * what makes offline playback work.
     */
    fun getCacheDataSourceFactory(context: Context): DataSource.Factory {
        val upstream = DefaultDataSource.Factory(context.applicationContext, getHttpDataSourceFactory())
        // NOTE: do not call setCacheWriteDataSinkFactory(null) — that makes the cache
        // read-only. Leaving it default installs a write sink, so streaming fills the
        // cache (cache-while-playing) and completed downloads play back from it offline.
        return CacheDataSource.Factory()
            .setCache(getCache(context))
            .setUpstreamDataSourceFactory(upstream)
            .setFlags(CacheDataSource.FLAG_IGNORE_CACHE_ON_ERROR)
    }

    // ---- Background prefetch (cache-the-whole-file-while-playing) ----------------

    /**
     * Process-wide priority arbiter shared by playback and the background prefetch. The player
     * registers [C.PRIORITY_PLAYBACK] while it is actively loading (see PlayerActivity); the
     * prefetch reads at the lower [C.PRIORITY_DOWNLOAD] and *blocks* whenever playback is loading.
     * Net effect: the prefetch runs full speed in the gaps but instantly yields the network the
     * moment live playback needs it, so it can never *cause* a rebuffer.
     */
    fun getPriorityTaskManager(): PriorityTaskManager =
        priorityTaskManager ?: synchronized(lock) {
            priorityTaskManager ?: PriorityTaskManager().also { priorityTaskManager = it }
        }

    /**
     * Cache-writing data source factory used ONLY by [buildPrefetchWriter]. Same shared cache and
     * key scheme as playback, but its network reads pass through the priority gate above. Because
     * writes land in the same [SimpleCache] under the same video-id key, prefetched bytes are
     * reused by the player and by a later explicit download — fetched once, never duplicated.
     */
    private fun getPrefetchCacheDataSourceFactory(context: Context): CacheDataSource.Factory {
        val yieldingUpstream = PriorityYieldDataSourceFactory(
            DefaultDataSource.Factory(context.applicationContext, getHttpDataSourceFactory()),
            getPriorityTaskManager(),
            C.PRIORITY_DOWNLOAD
        )
        return CacheDataSource.Factory()
            .setCache(getCache(context))
            .setUpstreamDataSourceFactory(yieldingUpstream)
    }

    /**
     * A [CacheWriter] that pulls the ENTIRE [uri] into the shared cache under [cacheKey] (the
     * stable video id). Caller runs [CacheWriter.cache] on a background thread and calls
     * [CacheWriter.cancel] to stop it (e.g. when the player closes). Bytes already cached by
     * playback are skipped, so this only ever fetches the not-yet-watched remainder.
     *
     * [onProgress] (optional) reports caching progress as the writer runs:
     * `requestLength` = total bytes to cache (the content length, or [C.LENGTH_UNSET] when
     * unknown), `bytesCached` = total cached so far, `newBytesCached` = bytes added by this tick.
     * It is invoked on the caller's (background) thread.
     */
    fun buildPrefetchWriter(
        context: Context,
        uri: String,
        cacheKey: String,
        onProgress: ((requestLength: Long, bytesCached: Long, newBytesCached: Long) -> Unit)? = null
    ): CacheWriter {
        val cacheDataSource = getPrefetchCacheDataSourceFactory(context).createDataSourceForDownloading()
        val dataSpec = DataSpec.Builder()
            .setUri(android.net.Uri.parse(uri))
            .setKey(cacheKey)
            .build()
        val listener = onProgress?.let {
            CacheWriter.ProgressListener { requestLength, bytesCached, newBytesCached ->
                it(requestLength, bytesCached, newBytesCached)
            }
        }
        return CacheWriter(cacheDataSource, dataSpec, /* temporaryBuffer = */ null, listener)
    }

    fun getDownloadManager(context: Context): DownloadManager =
        downloadManager ?: synchronized(lock) {
            downloadManager ?: DownloadManager(
                context.applicationContext,
                getDatabaseProvider(context),
                getCache(context),
                getHttpDataSourceFactory(),
                Executors.newFixedThreadPool(MAX_PARALLEL_DOWNLOADS)
            ).apply {
                maxParallelDownloads = MAX_PARALLEL_DOWNLOADS
            }.also { downloadManager = it }
        }

    fun getDownloadNotificationHelper(context: Context): DownloadNotificationHelper =
        notificationHelper ?: synchronized(lock) {
            notificationHelper ?: DownloadNotificationHelper(
                context.applicationContext,
                DOWNLOAD_NOTIFICATION_CHANNEL_ID
            ).also { notificationHelper = it }
        }

    // ---- Convenience over the download index -------------------------------------

    /** The current [Download] for a video id, or null if it was never downloaded. */
    fun getDownload(context: Context, videoId: String): Download? =
        runCatching { getDownloadManager(context).downloadIndex.getDownload(videoId) }.getOrNull()

    fun isDownloaded(context: Context, videoId: String): Boolean =
        getDownload(context, videoId)?.state == Download.STATE_COMPLETED

    // ---- Cache size / cleanup (for the Storage screen) ---------------------

    /** Total bytes currently held in the shared media cache (streamed + downloaded). */
    fun cacheUsedBytes(context: Context): Long =
        runCatching { getCache(context).cacheSpace }.getOrDefault(0L)

    /** Cache keys = stable video ids that have any cached bytes. */
    fun cachedKeys(context: Context): Set<String> =
        runCatching { getCache(context).keys }.getOrDefault(emptySet())

    /** Bytes cached for a single video id. */
    fun cachedBytesFor(context: Context, key: String): Long =
        runCatching { getCache(context).getCachedBytes(key, 0, Long.MAX_VALUE) }.getOrDefault(0L)

    /**
     * Evict a purely-streamed (non-download) item from the cache. For a completed download
     * use [Downloads.remove] instead so the download index stays consistent.
     */
    fun removeFromCache(context: Context, key: String) {
        runCatching {
            val cache = getCache(context)
            // Iterate spans (guaranteed Cache API) and drop each; unlocked streamed spans
            // delete cleanly. Download spans are locked, so callers route those via Downloads.
            for (span in cache.getCachedSpans(key)) {
                runCatching { cache.removeSpan(span) }
            }
        }
    }

    /** All downloads (completed + in-progress), newest first — usable offline. */
    fun listDownloads(context: Context): List<Download> {
        val result = mutableListOf<Download>()
        runCatching {
            getDownloadManager(context).downloadIndex.getDownloads().use { cursor ->
                while (cursor.moveToNext()) result.add(cursor.download)
            }
        }
        return result.sortedByDescending { it.updateTimeMs }
    }

    /** Build the [DownloadRequest] for a video, stashing offline metadata in `data`. */
    fun buildRequest(detail: VideoDetail): DownloadRequest {
        val meta = DownloadMetadata(
            id = detail.id,
            title = detail.title,
            thumbnailUrl = detail.thumbnailUrl,
            mimeType = detail.mimeType,
            uri = detail.playbackUrl
        )
        return DownloadRequest.Builder(detail.id, android.net.Uri.parse(detail.playbackUrl))
            .setCustomCacheKey(detail.id)
            .setMimeType(detail.mimeType)
            .setData(json.encodeToString(DownloadMetadata.serializer(), meta).toByteArray())
            .build()
    }
}

/** Wraps an upstream factory so every created source yields to higher-priority playback. */
@UnstableApi
private class PriorityYieldDataSourceFactory(
    private val upstreamFactory: DataSource.Factory,
    private val priorityTaskManager: PriorityTaskManager,
    private val priority: Int
) : DataSource.Factory {
    override fun createDataSource(): DataSource =
        PriorityYieldDataSource(upstreamFactory.createDataSource(), priorityTaskManager, priority)
}

/**
 * A [DataSource] that blocks before each open/read until no higher-priority task (i.e. live
 * playback) is registered with the [PriorityTaskManager]. This is what makes a full-speed
 * prefetch politely step aside the instant the player needs the network.
 */
@UnstableApi
private class PriorityYieldDataSource(
    private val upstream: DataSource,
    private val priorityTaskManager: PriorityTaskManager,
    private val priority: Int
) : DataSource {
    override fun addTransferListener(transferListener: TransferListener) {
        upstream.addTransferListener(transferListener)
    }

    override fun open(dataSpec: DataSpec): Long {
        awaitTurn()
        return upstream.open(dataSpec)
    }

    override fun read(buffer: ByteArray, offset: Int, length: Int): Int {
        awaitTurn()
        return upstream.read(buffer, offset, length)
    }

    override fun getUri(): android.net.Uri? = upstream.uri

    override fun getResponseHeaders(): Map<String, List<String>> = upstream.responseHeaders

    override fun close() = upstream.close()

    /** Block while higher-priority playback is loading; bail out cleanly if interrupted/cancelled. */
    private fun awaitTurn() {
        try {
            priorityTaskManager.proceed(priority)
        } catch (e: InterruptedException) {
            Thread.currentThread().interrupt()
            throw InterruptedIOException()
        }
    }
}
