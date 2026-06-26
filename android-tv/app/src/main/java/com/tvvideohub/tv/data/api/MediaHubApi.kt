package com.tvvideohub.tv.data.api

import com.tvvideohub.tv.data.dto.AppRelease
import com.tvvideohub.tv.data.dto.VideoDetail
import com.tvvideohub.tv.data.dto.VideoListResponse
import retrofit2.Response
import retrofit2.http.GET
import retrofit2.http.Path

/** Retrofit description of the backend API. See repo README "API contract (v1)". */
interface MediaHubApi {

    /** Liveness probe used to validate a configured base URL. */
    @GET("api/health")
    suspend fun health(): Response<Unit>

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
