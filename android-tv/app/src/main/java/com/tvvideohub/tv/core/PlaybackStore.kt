package com.tvvideohub.tv.core

import android.content.Context

/**
 * Remembers the last playback position per video so reopening an episode resumes where the user
 * left off.
 *
 * Keyed by the **stable video id** in a dedicated SharedPreferences file — additive on-device
 * state that never touches existing settings, the download index, or the media cache. The entry
 * is cleared once a video is effectively finished, so a fully-watched episode starts over.
 */
object PlaybackStore {
    private const val PREFS = "playback_positions"
    private const val MIN_SAVE_MS = 3_000L          // ignore the first few seconds (not worth resuming)
    private const val END_THRESHOLD_MS = 5_000L     // within 5s of the end == finished → start over

    private fun prefs(context: Context) =
        context.applicationContext.getSharedPreferences(PREFS, Context.MODE_PRIVATE)

    /** Saved resume position for [id] in ms, or 0 if none. */
    fun positionFor(context: Context, id: String): Long =
        if (id.isBlank()) 0L else prefs(context).getLong(id, 0L)

    /** Persist [positionMs] for [id]; clears it instead when playback is effectively finished. */
    fun save(context: Context, id: String, positionMs: Long, durationMs: Long) {
        if (id.isBlank()) return
        val finished = durationMs > 0L && positionMs >= durationMs - END_THRESHOLD_MS
        if (finished || positionMs < MIN_SAVE_MS) prefs(context).edit().remove(id).apply()
        else prefs(context).edit().putLong(id, positionMs).apply()
    }

    fun clear(context: Context, id: String) {
        if (id.isNotBlank()) prefs(context).edit().remove(id).apply()
    }
}
