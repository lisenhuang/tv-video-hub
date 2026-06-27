package com.tvvideohub.tv.data.dto

import kotlinx.serialization.Serializable

/**
 * DTOs mirroring the backend HTTP/JSON contract (see repo root README.md, "API contract v1").
 * Field names MUST match the JSON exactly. Nullable fields are nullable here too.
 */

/** Wrapper for `GET /api/videos`. */
@Serializable
data class VideoListResponse(
    val videos: List<VideoSummary> = emptyList()
)

/** One item in `GET /api/videos`. */
@Serializable
data class VideoSummary(
    val id: String,
    val title: String,
    val description: String = "",
    val thumbnailUrl: String? = null,
    val durationSeconds: Int? = null,
    val createdAt: String
)

/** `GET /api/videos/{id}` — details plus a short-lived playback URL. */
@Serializable
data class VideoDetail(
    val id: String,
    val title: String,
    val description: String = "",
    val thumbnailUrl: String? = null,
    val durationSeconds: Int? = null,
    val playbackUrl: String,
    val playbackUrlExpiresAt: String,
    val mimeType: String,
    val createdAt: String
)

/**
 * `GET /api/app/latest` — newest published APK.
 * Note: the endpoint returns HTTP 204 (no body) when no release exists yet;
 * the API layer maps that to a null AppRelease.
 */
@Serializable
data class AppRelease(
    val versionCode: Int,
    val versionName: String,
    val notes: String = "",
    val downloadUrl: String,
    val sizeBytes: Long,
    val sha256: String,
    val minSdk: Int,
    val publishedAt: String,
    /** When true the update is mandatory: the app must not let the user dismiss/cancel it. */
    val forceUpdate: Boolean = false
)

/**
 * `GET /api/app/access` — whether the app must present an access code, and whether the code it
 * sent (the `X-Access-Code` header) is currently valid. `required=false` ⇒ gate off, proceed.
 */
@Serializable
data class AccessStatus(
    val required: Boolean = false,
    val valid: Boolean = false
)
