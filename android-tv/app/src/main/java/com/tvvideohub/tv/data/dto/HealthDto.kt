package com.tvvideohub.tv.data.dto

import kotlinx.serialization.Serializable

/**
 * `GET /api/health` body. Used to verify a configured URL is genuinely a tv-video-hub
 * backend (not just any server that returns HTTP 200) by matching [service].
 */
@Serializable
data class HealthDto(
    val status: String = "",
    val service: String = "",
    val api: String = ""
) {
    val isThisBackend: Boolean get() = service.equals(SERVICE_ID, ignoreCase = true)

    companion object {
        const val SERVICE_ID = "tv-video-hub"
    }
}
