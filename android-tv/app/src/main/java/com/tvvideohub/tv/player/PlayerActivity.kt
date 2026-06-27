package com.tvvideohub.tv.player

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.view.Gravity
import android.view.WindowManager
import android.widget.FrameLayout
import android.widget.ProgressBar
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.annotation.OptIn
import androidx.core.view.isVisible
import androidx.lifecycle.lifecycleScope
import androidx.media3.common.C
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.datasource.cache.CacheWriter
import androidx.media3.exoplayer.DefaultLoadControl
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.ui.PlayerView
import com.tvvideohub.tv.core.LocaleHelper
import com.tvvideohub.tv.core.PlaybackStore
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.download.DownloadUtil
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.isActive
import kotlinx.coroutines.launch

/**
 * Full-screen ExoPlayer activity.
 *
 * Two entry modes:
 *  - **Online**: given only a video id, it fetches the short-lived `playbackUrl` from the
 *    backend and streams it. Streaming goes through the shared cache, so watched bytes are
 *    retained for instant re-watch / scrubbing.
 *  - **Offline**: given a direct URI (from a completed download), it plays straight from the
 *    cache with no network.
 *
 * Playback always uses `customCacheKey = videoId` so cached bytes are reused even though the
 * presigned URL rotates between sessions (and survive app upgrades).
 *
 * Input: PlayerView's controller stays enabled (useController=true) for touch users and maps
 * remote keys (DPAD_CENTER play/pause, DPAD left/right seek). The view grabs focus on entry
 * so a remote works immediately; BACK finishes the activity.
 */
@OptIn(UnstableApi::class)
class PlayerActivity : ComponentActivity() {

    private val repository = CatalogRepository()

    private var player: ExoPlayer? = null
    private lateinit var playerView: PlayerView
    private lateinit var messageView: TextView
    // Whole-episode disk-cache progress indicator (separate from the player's seek-bar buffer tick,
    // which only shows the in-memory forward buffer). Shown while caching is 1..99%, hidden at 100%.
    private lateinit var cacheBar: ProgressBar
    private lateinit var cacheLabel: TextView

    // Background prefetch: caches the whole file while it plays so playback doesn't stall on a
    // slow link. Best-effort and cancellable — it never affects the player if it fails.
    @Volatile private var prefetchWriter: CacheWriter? = null
    private var prefetchJob: Job? = null
    // Set when the player is tearing down the prefetch, so the retry loop bails instead of
    // mistaking a cancel() for a network error and re-fetching.
    @Volatile private var prefetchCancelled = false
    // Last whole-percent reported to the UI, to avoid spamming the main thread on every read.
    @Volatile private var lastCachePct = -1
    // True while we hold PRIORITY_PLAYBACK in the shared PriorityTaskManager (so the prefetch yields).
    private var holdingPlaybackPriority = false

    // The currently-playing video id. Mutable: catalog playback loops to the next video
    // (wrapping past the last), so this advances as we move through the list.
    private var currentVideoId: String = ""
    private val directUri: String? by lazy { intent.getStringExtra(EXTRA_DIRECT_URI) }
    private val directMime: String? by lazy { intent.getStringExtra(EXTRA_DIRECT_MIME) }

    // Ordered catalog ids for loop playback (online only): >1 → loop the whole list and wrap;
    // <=1 (or offline) → repeat the single video via REPEAT_MODE_ONE. Empty until loaded.
    private var playlist: List<String> = emptyList()
    private var playlistIndex: Int = 0
    private val loopWholeList: Boolean get() = directUri.isNullOrBlank() && playlist.size > 1

    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, SettingsStore.get(newBase).language.value))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        currentVideoId = intent.getStringExtra(EXTRA_VIDEO_ID).orEmpty()

        val root = FrameLayout(this)
        playerView = PlayerView(this).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT
            )
            useController = true
            controllerShowTimeoutMs = 4000
            setShowBuffering(PlayerView.SHOW_BUFFERING_WHEN_PLAYING)
            isFocusable = true
            isFocusableInTouchMode = true
        }
        messageView = TextView(this).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT,
                Gravity.CENTER
            )
            setTextColor(0xFFE6EAF2.toInt())
            textSize = 18f
            text = getString(com.tvvideohub.tv.R.string.player_loading)
        }
        // Disk-cache progress: a thin full-width bar pinned to the TOP (kept clear of the player's
        // bottom seek bar) plus a "Caching NN%" label. Driven by the background prefetch, so it
        // keeps advancing to 100% even while the video is paused.
        val barHeightPx = (6 * resources.displayMetrics.density).toInt()
        cacheBar = ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                barHeightPx,
                Gravity.TOP
            )
            max = 100
            isVisible = false
        }
        cacheLabel = TextView(this).apply {
            layoutParams = FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.WRAP_CONTENT,
                FrameLayout.LayoutParams.WRAP_CONTENT,
                Gravity.TOP or Gravity.START
            ).apply { setMargins(barHeightPx * 3, barHeightPx * 3, 0, 0) }
            setTextColor(0xFFE6EAF2.toInt())
            textSize = 12f
            isVisible = false
        }
        root.addView(playerView)
        root.addView(messageView)
        root.addView(cacheBar)
        root.addView(cacheLabel)
        setContentView(root)

        if (currentVideoId.isBlank() && directUri.isNullOrBlank()) {
            showMessage(getString(com.tvvideohub.tv.R.string.player_error))
            return
        }

        val uri = directUri
        if (!uri.isNullOrBlank()) {
            // Offline / direct playback — no network round-trip. Repeats the one video.
            preparePlayer(uri, directMime, cacheKey = currentVideoId.ifBlank { uri })
        } else {
            loadAndPlay()
        }
    }

    private fun loadAndPlay() {
        lifecycleScope.launch {
            try {
                val detail = repository.getVideo(currentVideoId)
                // Build the loop playlist from the catalog order (best-effort). With >1 video
                // we loop the whole list; with 0/1 we repeat the current one (REPEAT_MODE_ONE).
                runCatching {
                    val ids = repository.listVideos().map { it.id }
                    if (ids.isNotEmpty()) {
                        playlist = ids
                        playlistIndex = ids.indexOf(currentVideoId).coerceAtLeast(0)
                    }
                }
                preparePlayer(detail.playbackUrl, detail.mimeType, cacheKey = detail.id)
            } catch (t: Throwable) {
                // If we're offline but the video was downloaded, fall back to the cache.
                val offline = DownloadUtil.getDownload(this@PlayerActivity, currentVideoId)
                if (offline != null) {
                    preparePlayer(offline.request.uri.toString(), offline.request.mimeType, cacheKey = currentVideoId)
                } else {
                    showMessage(getString(com.tvvideohub.tv.R.string.player_error))
                }
            }
        }
    }

    /**
     * Catalog loop: advance to the next video (wrapping past the last) and play it from the
     * start. Only used when [loopWholeList] is true (online, >1 video).
     */
    private fun advanceToNext() {
        if (playlist.isEmpty()) return
        playlistIndex = (playlistIndex + 1) % playlist.size
        currentVideoId = playlist[playlistIndex]
        lifecycleScope.launch {
            try {
                val detail = repository.getVideo(currentVideoId)
                preparePlayer(detail.playbackUrl, detail.mimeType, cacheKey = detail.id, resumeAllowed = false)
            } catch (t: Throwable) {
                showMessage(getString(com.tvvideohub.tv.R.string.player_error))
            }
        }
    }

    private fun preparePlayer(uri: String, mime: String?, cacheKey: String, resumeAllowed: Boolean = true) {
        // Release any previous player/prefetch first, so this is safe to call again when the
        // catalog loop advances to the next video.
        releasePlayer()

        // Route all reads through the shared cache so streaming fills the cache and a
        // completed download plays without touching the network.
        val mediaSourceFactory = DefaultMediaSourceFactory(this)
            .setDataSourceFactory(DownloadUtil.getCacheDataSourceFactory(this))

        // Buffer up to ~5 minutes ahead (vs Media3's ~50s default) so the seek-bar buffered tick
        // extends far ahead and keeps filling while paused. Bounded by a hard byte ceiling so a
        // high-bitrate stream can't OOM a low-RAM TV: the loader stops at whichever limit hits
        // first (300s at typical bitrate ≈ tens of MB; the 128 MB cap protects the worst case).
        // The whole episode still reaches disk via the background prefetch below.
        val loadControl = DefaultLoadControl.Builder()
            .setBufferDurationsMs(
                /* minBufferMs = */ 50_000,
                /* maxBufferMs = */ 300_000,
                /* bufferForPlaybackMs = */ 2_500,
                /* bufferForPlaybackAfterRebufferMs = */ 5_000
            )
            .setTargetBufferBytes(128 * 1024 * 1024)
            .setPrioritizeTimeOverSizeThresholds(false)
            .build()

        val exo = ExoPlayer.Builder(this)
            .setMediaSourceFactory(mediaSourceFactory)
            .setLoadControl(loadControl)
            .build()
        player = exo
        playerView.player = exo
        // A single video (or offline playback) repeats seamlessly; a multi-video catalog loops
        // via manual advance on STATE_ENDED so each next presigned URL is fetched fresh.
        exo.repeatMode = if (loopWholeList) Player.REPEAT_MODE_OFF else Player.REPEAT_MODE_ONE

        val mediaItem = MediaItem.Builder()
            .setUri(uri)
            .setCustomCacheKey(cacheKey)
            .apply { mapMimeType(mime)?.let { setMimeType(it) } }
            .build()

        exo.addListener(object : Player.Listener {
            override fun onPlayerError(error: PlaybackException) {
                showMessage(getString(com.tvvideohub.tv.R.string.player_error))
            }

            override fun onPlaybackStateChanged(playbackState: Int) {
                if (playbackState == Player.STATE_READY || playbackState == Player.STATE_BUFFERING) {
                    hideMessage()
                }
                // Finished watching → forget the resume point so it starts over next time.
                if (playbackState == Player.STATE_ENDED) {
                    PlaybackStore.clear(this@PlayerActivity, currentVideoId)
                    // Catalog loop: roll on to the next video (wraps past the last). A single
                    // video repeats via REPEAT_MODE_ONE and never reaches STATE_ENDED.
                    if (loopWholeList) advanceToNext()
                }
                // Yield the background prefetch to playback ONLY while we're actually starved
                // (rebuffering). During normal READY playback the prefetch keeps caching ahead at
                // full speed, building a buffer so a slow patch doesn't stall the picture. (Yielding
                // on every "loading" tick, as before, starved the prefetch on slow networks — the
                // player loads almost constantly there, so the cache never got ahead.)
                setPlaybackStarved(playbackState == Player.STATE_BUFFERING)
            }

            override fun onIsPlayingChanged(isPlaying: Boolean) {
                // Tell the system we're busy while playing so the TV doesn't think it's idle
                // and start the screensaver/daydream mid-video. Released when paused/stopped/ended
                // so the screensaver can still come up when the device is genuinely idle.
                setKeepScreenOn(isPlaying)
            }
        })

        // Resume where we left off last time (same episode, keyed by stable video id). When the
        // loop advances to the next video we start it from the beginning (resumeAllowed=false).
        val resumeMs = if (resumeAllowed) PlaybackStore.positionFor(this, currentVideoId) else 0L
        exo.setMediaItem(mediaItem, if (resumeMs > 0L) resumeMs else C.TIME_UNSET)
        exo.playWhenReady = true
        exo.prepare()
        playerView.requestFocus()

        // Online streaming only: eagerly pull the whole file into the shared cache so playback
        // won't stall on a slow/bursty link. Offline/direct playback is already fully cached.
        if (directUri.isNullOrBlank()) startPrefetch(uri, cacheKey)
    }

    /**
     * Kick off a full-speed background prefetch of [uri] into the shared cache under [cacheKey].
     * Reuses any bytes playback already cached (no re-fetch, no duplication) and yields to the
     * player via the shared [DownloadUtil.getPriorityTaskManager]. Best-effort: failures are
     * swallowed so they can never disrupt playback.
     *
     * Runs to 100% even on long episodes: the playback URL is short-lived/presigned and can expire
     * mid-prefetch, so on a (non-cancel) failure we re-fetch a fresh URL via the API and resume.
     * The cache is keyed by stable video id, so the resumed writer skips already-cached bytes —
     * each byte is still fetched at most once.
     */
    private fun startPrefetch(uri: String, cacheKey: String) {
        if (cacheKey.isBlank()) return
        cancelPrefetch()
        prefetchCancelled = false
        lastCachePct = -1
        prefetchJob = lifecycleScope.launch(Dispatchers.IO) {
            var currentUri = uri
            var attempt = 0
            while (isActive && !prefetchCancelled) {
                try {
                    val writer = DownloadUtil.buildPrefetchWriter(applicationContext, currentUri, cacheKey) {
                        requestLength, bytesCached, _ -> onCacheProgress(requestLength, bytesCached)
                    }
                    prefetchWriter = writer
                    writer.cache() // blocks until fully cached, cancelled, or a network error
                    return@launch   // whole file cached
                } catch (_: Throwable) {
                    // Player closing → bail without re-fetching. Otherwise the URL likely expired or
                    // the network hiccuped: get a fresh URL and resume (retry-capped), else back off.
                    if (prefetchCancelled || !isActive) return@launch
                    if (attempt++ >= MAX_PREFETCH_RETRIES) return@launch
                    val fresh = runCatching { repository.getVideo(currentVideoId).playbackUrl }.getOrNull()
                    if (!fresh.isNullOrBlank()) currentUri = fresh else delay(PREFETCH_RETRY_DELAY_MS)
                }
            }
        }
    }

    private fun cancelPrefetch() {
        prefetchCancelled = true
        prefetchWriter?.let { runCatching { it.cancel() } }
        prefetchWriter = null
        prefetchJob?.cancel()
        prefetchJob = null
    }

    /**
     * Prefetch progress (called on the prefetch thread). Maps cached/total to a whole percent and,
     * only when it changes, updates the on-screen cache indicator on the UI thread.
     */
    private fun onCacheProgress(requestLength: Long, bytesCached: Long) {
        if (requestLength <= 0L) return // total unknown — can't show a percentage
        val pct = ((bytesCached * 100L) / requestLength).toInt().coerceIn(0, 100)
        if (pct == lastCachePct) return
        lastCachePct = pct
        runOnUiThread { updateCacheIndicator(pct) }
    }

    /** Show the cache bar + label while caching is in progress (1..99%); hide it at 0/100%. */
    private fun updateCacheIndicator(pct: Int) {
        val inProgress = pct in 1..99
        if (inProgress) {
            cacheBar.progress = pct
            cacheLabel.text = getString(com.tvvideohub.tv.R.string.player_caching_percent, pct)
        }
        cacheBar.isVisible = inProgress
        cacheLabel.isVisible = inProgress
    }

    /**
     * Acquire/release PRIORITY_PLAYBACK exactly once (kept add/remove balanced). While held, the
     * background prefetch blocks so live playback gets the whole pipe; we only hold it while the
     * player is genuinely starved (rebuffering).
     */
    private fun setPlaybackStarved(starved: Boolean) {
        val ptm = DownloadUtil.getPriorityTaskManager()
        if (starved && !holdingPlaybackPriority) {
            ptm.add(C.PRIORITY_PLAYBACK)
            holdingPlaybackPriority = true
        } else if (!starved && holdingPlaybackPriority) {
            ptm.remove(C.PRIORITY_PLAYBACK)
            holdingPlaybackPriority = false
        }
    }

    private fun mapMimeType(mime: String?): String? = when (mime?.lowercase()) {
        "video/mp4", "application/mp4" -> MimeTypes.VIDEO_MP4
        "application/x-mpegurl", "application/vnd.apple.mpegurl" -> MimeTypes.APPLICATION_M3U8
        "application/dash+xml" -> MimeTypes.APPLICATION_MPD
        "video/webm" -> MimeTypes.VIDEO_WEBM
        else -> null
    }

    private fun showMessage(text: String) { messageView.text = text; messageView.isVisible = true }
    private fun hideMessage() { messageView.isVisible = false }

    /**
     * Hold/release the keep-screen-on window flag. While held, the display stays awake and the
     * Android TV screensaver/daydream is suppressed — that's what stops the TV from going idle
     * during playback.
     */
    private fun setKeepScreenOn(on: Boolean) {
        if (on) window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
        else window.clearFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)
    }

    override fun onStart() { super.onStart(); player?.playWhenReady = true }
    override fun onStop() {
        super.onStop()
        // Remember the position before we pause, so closing/reopening resumes here.
        saveProgress()
        player?.playWhenReady = false
    }
    override fun onDestroy() { super.onDestroy(); releasePlayer() }

    /** Persist the current position for [videoId] (cleared automatically once finished). */
    private fun saveProgress() {
        val p = player ?: return
        if (currentVideoId.isBlank()) return
        PlaybackStore.save(this, currentVideoId, p.currentPosition, p.duration)
    }

    private fun releasePlayer() {
        cancelPrefetch()
        setPlaybackStarved(false) // release any held playback priority so the manager stays balanced
        playerView.player = null
        player?.release()
        player = null
    }

    companion object {
        private const val EXTRA_VIDEO_ID = "extra_video_id"
        private const val EXTRA_DIRECT_URI = "extra_direct_uri"
        private const val EXTRA_DIRECT_MIME = "extra_direct_mime"

        // Prefetch resilience: how many times to re-fetch a fresh URL + resume before giving up,
        // and how long to back off when a fresh URL can't be obtained.
        private const val MAX_PREFETCH_RETRIES = 5
        private const val PREFETCH_RETRY_DELAY_MS = 2_000L

        /** Online playback by video id (fetches a fresh playback URL). */
        fun intent(context: Context, videoId: String): Intent =
            Intent(context, PlayerActivity::class.java).putExtra(EXTRA_VIDEO_ID, videoId)

        /** Offline playback from a downloaded item (no network). */
        fun offlineIntent(context: Context, videoId: String, uri: String, mimeType: String?): Intent =
            Intent(context, PlayerActivity::class.java)
                .putExtra(EXTRA_VIDEO_ID, videoId)
                .putExtra(EXTRA_DIRECT_URI, uri)
                .putExtra(EXTRA_DIRECT_MIME, mimeType)
    }
}
