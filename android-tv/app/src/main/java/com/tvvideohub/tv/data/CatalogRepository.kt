package com.tvvideohub.tv.data

import com.tvvideohub.tv.data.api.ApiClient
import com.tvvideohub.tv.data.api.MediaHubApi
import com.tvvideohub.tv.data.dto.AppRelease
import com.tvvideohub.tv.data.dto.VideoDetail
import com.tvvideohub.tv.data.dto.VideoSummary
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/**
 * Thin data layer over [MediaHubApi]. Keeps networking off the main thread and exposes
 * suspend functions returning plain DTOs (errors propagate as exceptions to callers).
 */
class CatalogRepository(
    private val api: MediaHubApi = ApiClient.service
) {

    /** True if the configured backend answers its health probe. */
    suspend fun isBackendReachable(): Boolean = withContext(Dispatchers.IO) {
        runCatching { api.health().isSuccessful }.getOrDefault(false)
    }

    suspend fun listVideos(): List<VideoSummary> = withContext(Dispatchers.IO) {
        api.getVideos().videos
    }

    suspend fun getVideo(id: String): VideoDetail = withContext(Dispatchers.IO) {
        api.getVideo(id)
    }

    /** Returns the latest release, or null if the backend has none (HTTP 204). */
    suspend fun getLatestRelease(): AppRelease? = withContext(Dispatchers.IO) {
        val response = api.getLatestRelease()
        if (response.code() == 204) return@withContext null
        if (!response.isSuccessful) {
            throw IllegalStateException("Failed to fetch latest release: HTTP ${response.code()}")
        }
        response.body()
    }
}
