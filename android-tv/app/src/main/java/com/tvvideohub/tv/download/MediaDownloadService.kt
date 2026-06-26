package com.tvvideohub.tv.download

import android.app.Notification
import androidx.media3.common.util.NotificationUtil
import androidx.media3.common.util.UnstableApi
import androidx.media3.exoplayer.offline.Download
import androidx.media3.exoplayer.offline.DownloadManager
import androidx.media3.exoplayer.offline.DownloadService
import androidx.media3.exoplayer.scheduler.Scheduler
import com.tvvideohub.tv.R

/**
 * Foreground service that runs offline downloads via Media3's [DownloadManager].
 * Started indirectly through `DownloadService.sendAddDownload(...)` /
 * `sendRemoveDownload(...)`. Shows a progress notification while downloading.
 */
@UnstableApi
class MediaDownloadService : DownloadService(
    FOREGROUND_NOTIFICATION_ID,
    DEFAULT_FOREGROUND_NOTIFICATION_UPDATE_INTERVAL,
    DownloadUtil.DOWNLOAD_NOTIFICATION_CHANNEL_ID,
    R.string.download_channel_name,
    /* channelDescriptionResourceId = */ 0
) {
    override fun getDownloadManager(): DownloadManager =
        DownloadUtil.getDownloadManager(this)

    // No background scheduler: downloads run while the app process is alive. Good enough
    // for a TV/phone app driven from the UI; can be upgraded to a PlatformScheduler later.
    override fun getScheduler(): Scheduler? = null

    override fun getForegroundNotification(
        downloads: MutableList<Download>,
        notMetRequirements: Int
    ): Notification =
        DownloadUtil.getDownloadNotificationHelper(this).buildProgressNotification(
            /* context = */ this,
            /* smallIcon = */ R.drawable.ic_download,
            /* contentIntent = */ null,
            /* message = */ if (downloads.isEmpty()) null else downloads.first().request.id,
            downloads,
            notMetRequirements
        )

    private companion object {
        const val FOREGROUND_NOTIFICATION_ID = 4711
    }
}

/** Helpers to enqueue/cancel downloads from anywhere with an application context. */
@UnstableApi
object Downloads {
    fun start(context: android.content.Context, detail: com.tvvideohub.tv.data.dto.VideoDetail) {
        DownloadService.sendAddDownload(
            context,
            MediaDownloadService::class.java,
            DownloadUtil.buildRequest(detail),
            /* foreground = */ false
        )
    }

    fun remove(context: android.content.Context, videoId: String) {
        DownloadService.sendRemoveDownload(
            context,
            MediaDownloadService::class.java,
            videoId,
            /* foreground = */ false
        )
    }

    /** Ensure the download notification channel exists (call once at startup). */
    fun ensureNotificationChannel(context: android.content.Context) {
        NotificationUtil.createNotificationChannel(
            context,
            DownloadUtil.DOWNLOAD_NOTIFICATION_CHANNEL_ID,
            R.string.download_channel_name,
            /* nameResourceId description */ 0,
            NotificationUtil.IMPORTANCE_LOW
        )
    }
}
