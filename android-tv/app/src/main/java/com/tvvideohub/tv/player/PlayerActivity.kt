package com.tvvideohub.tv.player

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.view.WindowManager
import android.widget.FrameLayout
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

    // Background prefetch: caches the whole file while it plays so playback doesn't stall on a
    // slow link. Best-effort and cancellable — it never affects the player if it fails.
    @Volatile private var prefetchWriter: CacheWriter? = null
    private var prefetchJob: Job? = null
    // True while we hold PRIORITY_PLAYBACK in the shared PriorityTaskManager (so the prefetch yields).
    private var holdingPlaybackPriority = false

    private val videoId: String by lazy { intent.getStringExtra(EXTRA_VIDEO_ID).orEmpty() }
    private val directUri: String? by lazy { intent.getStringExtra(EXTRA_DIRECT_URI) }
    private val directMime: String? by lazy { intent.getStringExtra(EXTRA_DIRECT_MIME) }

    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, SettingsStore.get(newBase).language.value))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

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
                android.view.Gravity.CENTER
            )
            setTextColor(0xFFE6EAF2.toInt())
            textSize = 18f
            text = getString(com.tvvideohub.tv.R.string.player_loading)
        }
        root.addView(playerView)
        root.addView(messageView)
        setContentView(root)

        if (videoId.isBlank() && directUri.isNullOrBlank()) {
            showMessage(getString(com.tvvideohub.tv.R.string.player_error))
            return
        }

        val uri = directUri
        if (!uri.isNullOrBlank()) {
            // Offline / direct playback — no network round-trip.
            preparePlayer(uri, directMime, cacheKey = videoId.ifBlank { uri })
        } else {
            loadAndPlay()
        }
    }

    private fun loadAndPlay() {
        lifecycleScope.launch {
            try {
                val detail = repository.getVideo(videoId)
                preparePlayer(detail.playbackUrl, detail.mimeType, cacheKey = detail.id)
            } catch (t: Throwable) {
                // If we're offline but the video was downloaded, fall back to the cache.
                val offline = DownloadUtil.getDownload(this@PlayerActivity, videoId)
                if (offline != null) {
                    preparePlayer(offline.request.uri.toString(), offline.request.mimeType, cacheKey = videoId)
                } else {
                    showMessage(getString(com.tvvideohub.tv.R.string.player_error))
                }
            }
        }
    }

    private fun preparePlayer(uri: String, mime: String?, cacheKey: String) {
        // Route all reads through the shared cache so streaming fills the cache and a
        // completed download plays without touching the network.
        val mediaSourceFactory = DefaultMediaSourceFactory(this)
            .setDataSourceFactory(DownloadUtil.getCacheDataSourceFactory(this))

        val exo = ExoPlayer.Builder(this)
            .setMediaSourceFactory(mediaSourceFactory)
            .build()
        player = exo
        playerView.player = exo

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
                    PlaybackStore.clear(this@PlayerActivity, videoId)
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

        // Resume where we left off last time (same episode, keyed by stable video id).
        val resumeMs = PlaybackStore.positionFor(this, videoId)
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
     */
    private fun startPrefetch(uri: String, cacheKey: String) {
        if (cacheKey.isBlank()) return
        cancelPrefetch()
        prefetchJob = lifecycleScope.launch(Dispatchers.IO) {
            try {
                val writer = DownloadUtil.buildPrefetchWriter(applicationContext, uri, cacheKey)
                prefetchWriter = writer
                writer.cache() // blocks until fully cached, cancelled, or a network error
            } catch (_: Throwable) {
                // Cancellation or a network hiccup — playback is unaffected, so just stop quietly.
            }
        }
    }

    private fun cancelPrefetch() {
        prefetchWriter?.let { runCatching { it.cancel() } }
        prefetchWriter = null
        prefetchJob?.cancel()
        prefetchJob = null
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
        if (videoId.isBlank()) return
        PlaybackStore.save(this, videoId, p.currentPosition, p.duration)
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
