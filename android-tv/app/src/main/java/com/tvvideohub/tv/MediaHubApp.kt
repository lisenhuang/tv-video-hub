package com.tvvideohub.tv

import android.app.Application
import androidx.annotation.OptIn
import androidx.media3.common.util.UnstableApi
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.data.api.ApiClient
import com.tvvideohub.tv.download.DownloadUtil
import com.tvvideohub.tv.download.Downloads

/**
 * Application entry point.
 *  - Configures the API client from the persisted backend base URL (set on first run;
 *    null until then, which routes the UI to the setup screen).
 *  - Initializes the Media3 download manager (resumes any in-progress downloads from the
 *    on-device index) and ensures the download notification channel exists.
 */
class MediaHubApp : Application() {
    @OptIn(UnstableApi::class)
    override fun onCreate() {
        super.onCreate()

        // Configure networking from the persisted setting (may be null → not configured).
        ApiClient.configure(SettingsStore.get(this).baseUrl.value)

        // Touch the download manager so it resumes pending downloads; ensure the channel.
        Downloads.ensureNotificationChannel(this)
        DownloadUtil.getDownloadManager(this)
    }
}
