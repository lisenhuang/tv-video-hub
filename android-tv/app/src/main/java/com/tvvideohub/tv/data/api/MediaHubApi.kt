package com.tvvideohub.tv.data.api

import com.tvvideohub.tv.data.dto.AccessStatus
import com.tvvideohub.tv.data.dto.AppRelease
import com.tvvideohub.tv.data.dto.HealthDto
import com.tvvideohub.tv.data.dto.VideoDetail
import com.tvvideohub.tv.data.dto.VideoListResponse
import retrofit2.Response
import retrofit2.http.GET
import retrofit2.http.Path

/** Retrofit description of the backend API. See repo README "API contract (v1)". */
interface MediaHubApi {

    /** Health probe; its body carries the service identity used to validate a base URL. */
    @GET("api/health")
    suspend fun health(): Response<HealthDto>

    /** Whether the access-code gate is on and whether the sent X-Access-Code is valid. */
    @GET("api/app/access")
    suspend fun getAccessStatus(): AccessStatus

    @GET("api/videos")
    suspend fun getVideos(): VideoListResponse

    @GET("api/videos/{id}")
    suspend fun getVideo(@Path("id") id: String): VideoDetail

    /**
     * Latest published APK. Returns HTTP 204 (empty body) when there is no release yet,
     * so we use Response<AppRelease> to distinguish 204 from 200-with-body.
     */
    @GET("api/app/latest")
    suspend fun getLatestRelease(): Response<AppRelease>
}
