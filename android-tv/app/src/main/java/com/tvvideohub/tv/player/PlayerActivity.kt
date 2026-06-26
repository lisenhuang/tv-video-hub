package com.tvvideohub.tv.player

import android.content.Context
import android.content.Intent
import android.os.Bundle
import android.widget.FrameLayout
import android.widget.TextView
import androidx.activity.ComponentActivity
import androidx.annotation.OptIn
import androidx.core.view.isVisible
import androidx.lifecycle.lifecycleScope
import androidx.media3.common.MediaItem
import androidx.media3.common.MimeTypes
import androidx.media3.common.PlaybackException
import androidx.media3.common.Player
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.ExoPlayer
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory
import androidx.media3.ui.PlayerView
import com.tvvideohub.tv.core.LocaleHelper
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.download.DownloadUtil
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
            }
        })

        exo.setMediaItem(mediaItem)
        exo.playWhenReady = true
        exo.prepare()
        playerView.requestFocus()
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

    override fun onStart() { super.onStart(); player?.playWhenReady = true }
    override fun onStop() { super.onStop(); player?.playWhenReady = false }
    override fun onDestroy() { super.onDestroy(); releasePlayer() }

    private fun releasePlayer() {
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
