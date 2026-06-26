package com.tvvideohub.tv.data.api

import com.tvvideohub.tv.BuildConfig
import com.tvvideohub.tv.core.SettingsStore
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.logging.HttpLoggingInterceptor
import retrofit2.Retrofit
import retrofit2.converter.kotlinx.serialization.asConverterFactory
import java.util.concurrent.TimeUnit

/**
 * Retrofit client whose base URL is a **runtime setting** (configured on first launch and
 * changeable later), not a fixed build constant. [configure] rebuilds the client when the
 * URL changes; [service] returns the current API.
 *
 * The BuildConfig value is only an initial default a build can bake in; the persisted
 * [SettingsStore] value always wins once the user has set one.
 */
object ApiClient {

    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
        coerceInputValues = true
    }

    @Volatile private var currentBaseUrl: String? = null
    @Volatile private var api: MediaHubApi? = null

    /** True once a non-blank base URL has been configured. */
    val isConfigured: Boolean get() = !currentBaseUrl.isNullOrBlank()

    val baseUrl: String? get() = currentBaseUrl

    /** (Re)build the client for [baseUrl]. Safe to call repeatedly; no-ops if unchanged. */
    @Synchronized
    fun configure(baseUrl: String?) {
        val normalized = baseUrl?.takeIf { it.isNotBlank() }?.let { SettingsStore.normalize(it) }
        if (normalized == currentBaseUrl && api != null) return
        currentBaseUrl = normalized
        api = normalized?.let { build(it) }
    }

    /**
     * Current API. Throws [IllegalStateException] if not configured yet — callers in the
     * configured state won't hit this; the root flow gates UI on [isConfigured].
     */
    val service: MediaHubApi
        get() = api ?: error("ApiClient is not configured yet (no backend base URL set).")

    private fun build(baseUrl: String): MediaHubApi {
        val logging = HttpLoggingInterceptor().apply {
            level = if (BuildConfig.DEBUG) HttpLoggingInterceptor.Level.BASIC
            else HttpLoggingInterceptor.Level.NONE
        }
        val client = OkHttpClient.Builder()
            .connectTimeout(15, TimeUnit.SECONDS)
            .readTimeout(30, TimeUnit.SECONDS)
            .addInterceptor(logging)
            .build()

        val contentType = "application/json".toMediaType()
        return Retrofit.Builder()
            .baseUrl(baseUrl) // already normalized to end with '/'
            .client(client)
            .addConverterFactory(json.asConverterFactory(contentType))
            .build()
            .create(MediaHubApi::class.java)
    }
}
