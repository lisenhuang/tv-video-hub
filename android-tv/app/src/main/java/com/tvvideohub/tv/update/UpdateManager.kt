package com.tvvideohub.tv.update

import android.app.DownloadManager
import android.content.Context
import android.content.Intent
import android.database.Cursor
import android.net.Uri
import android.os.Build
import android.os.Environment
import android.provider.Settings
import androidx.core.content.FileProvider
import com.tvvideohub.tv.BuildConfig
import com.tvvideohub.tv.R
import com.tvvideohub.tv.data.dto.AppRelease
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.delay
import kotlinx.coroutines.withContext
import java.io.File
import java.security.MessageDigest

/**
 * Self-update flow:
 *  1. [checkForUpdate] compares the backend's latest [AppRelease].versionCode to this
 *     build's BuildConfig.VERSION_CODE.
 *  2. [downloadAndInstall] downloads the APK via [DownloadManager] into the app-specific
 *     external cache dir, verifies the SHA-256, then launches the package installer via a
 *     FileProvider content:// URI.
 *
 * On API 26+ the system requires the app to be allowed to "install unknown apps"
 * (REQUEST_INSTALL_PACKAGES alone is not enough). Callers should check
 * [canRequestInstalls] first and route the user to [unknownSourcesSettingsIntent] if false.
 */
class UpdateManager(private val context: Context) {

    sealed interface Result {
        /** Installer intent was launched; the OS now drives the install UI. */
        data object InstallLaunched : Result
        /** The app needs the "install unknown apps" permission before it can install. */
        data object NeedsInstallPermission : Result
        data class Failed(val message: String) : Result
    }

    /** Returns the release if it is strictly newer than the running build, else null. */
    fun isNewer(release: AppRelease?): AppRelease? =
        release?.takeIf { it.versionCode > BuildConfig.VERSION_CODE }

    /** True on API < 26, or when the user has granted "install unknown apps" to this app. */
    fun canRequestInstalls(): Boolean =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            context.packageManager.canRequestPackageInstalls()
        } else {
            true
        }

    /**
     * Intent that opens the per-app "install unknown apps" settings screen (API 26+),
     * or the legacy global "unknown sources" screen on older devices.
     */
    fun unknownSourcesSettingsIntent(): Intent =
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            Intent(
                Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:${context.packageName}")
            )
        } else {
            @Suppress("DEPRECATION")
            Intent(Settings.ACTION_SECURITY_SETTINGS)
        }

    /**
     * Downloads the APK, verifies its sha256, and launches the installer.
     * Must be called from a coroutine; blocking work runs on [Dispatchers.IO].
     */
    suspend fun downloadAndInstall(release: AppRelease): Result = withContext(Dispatchers.IO) {
        if (!canRequestInstalls()) return@withContext Result.NeedsInstallPermission

        val targetFile = apkFile(release.versionCode)
        // Reuse an already-downloaded, intact APK if present.
        if (!(targetFile.exists() && verifySha256(targetFile, release.sha256))) {
            targetFile.parentFile?.mkdirs()
            if (targetFile.exists()) targetFile.delete()

            val downloaded = try {
                downloadApk(release, targetFile)
            } catch (t: Throwable) {
                return@withContext Result.Failed(t.message ?: context.getString(R.string.update_error_download_failed))
            }
            if (!downloaded) {
                return@withContext Result.Failed(context.getString(R.string.update_error_download_incomplete))
            }
            if (!verifySha256(targetFile, release.sha256)) {
                targetFile.delete()
                return@withContext Result.Failed(context.getString(R.string.update_error_integrity))
            }
        }

        launchInstaller(targetFile)
        Result.InstallLaunched
    }

    /** Destination file inside the FileProvider-exposed external cache "updates/" dir. */
    private fun apkFile(versionCode: Int): File {
        val dir = File(context.externalCacheDir ?: context.cacheDir, "updates")
        return File(dir, "tv-video-hub-$versionCode.apk")
    }

    /**
     * Uses DownloadManager to fetch the APK into the app's external files area, then
     * copies the result into our FileProvider-mapped cache file. We poll the download
     * status from the coroutine rather than relying on a BroadcastReceiver.
     */
    private suspend fun downloadApk(release: AppRelease, dest: File): Boolean {
        val dm = context.getSystemService(Context.DOWNLOAD_SERVICE) as DownloadManager

        val request = DownloadManager.Request(Uri.parse(release.downloadUrl)).apply {
            setTitle(context.getString(R.string.update_notification_title, release.versionName))
            setMimeType("application/vnd.android.package-archive")
            setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE)
            setAllowedOverRoaming(true)
            // Download into the app-specific external files dir (no extra permission needed).
            setDestinationInExternalFilesDir(
                context,
                Environment.DIRECTORY_DOWNLOADS,
                "tv-video-hub-${release.versionCode}.apk"
            )
        }

        val downloadId = dm.enqueue(request)

        try {
            // Poll until terminal status (success or failure).
            while (true) {
                val (status, localUri, reason) = queryDownload(dm, downloadId)
                when (status) {
                    DownloadManager.STATUS_SUCCESSFUL -> {
                        val src = resolveLocalFile(localUri)
                            ?: return false
                        copyInto(src, dest)
                        // The intermediate DownloadManager file is no longer needed.
                        src.delete()
                        return true
                    }
                    DownloadManager.STATUS_FAILED -> {
                        throw IllegalStateException("DownloadManager failed (reason=$reason)")
                    }
                    -1 -> return false // query returned no row
                    else -> delay(POLL_INTERVAL_MS)
                }
            }
        } finally {
            // Best-effort cleanup of the DownloadManager entry.
            runCatching { dm.remove(downloadId) }
        }
    }

    private data class DownloadState(val status: Int, val localUri: String?, val reason: Int)

    private fun queryDownload(dm: DownloadManager, id: Long): DownloadState {
        val query = DownloadManager.Query().setFilterById(id)
        dm.query(query).use { cursor: Cursor ->
            if (!cursor.moveToFirst()) return DownloadState(-1, null, 0)
            val status = cursor.getIntOrZero(DownloadManager.COLUMN_STATUS)
            val localUri = cursor.getStringOrNull(DownloadManager.COLUMN_LOCAL_URI)
            val reason = cursor.getIntOrZero(DownloadManager.COLUMN_REASON)
            return DownloadState(status, localUri, reason)
        }
    }

    private fun Cursor.getIntOrZero(column: String): Int {
        val idx = getColumnIndex(column)
        return if (idx >= 0) getInt(idx) else 0
    }

    private fun Cursor.getStringOrNull(column: String): String? {
        val idx = getColumnIndex(column)
        return if (idx >= 0) getString(idx) else null
    }

    private fun resolveLocalFile(localUri: String?): File? {
        if (localUri == null) return null
        val uri = Uri.parse(localUri)
        return uri.path?.let { File(it) }?.takeIf { it.exists() }
    }

    private fun copyInto(src: File, dest: File) {
        dest.parentFile?.mkdirs()
        src.inputStream().use { input ->
            dest.outputStream().use { output ->
                input.copyTo(output)
            }
        }
    }

    /** Verifies the file's SHA-256 against the expected hex digest (case-insensitive). */
    private fun verifySha256(file: File, expectedHex: String): Boolean {
        if (!file.exists()) return false
        val digest = MessageDigest.getInstance("SHA-256")
        file.inputStream().use { input ->
            val buffer = ByteArray(8 * 1024)
            while (true) {
                val read = input.read(buffer)
                if (read < 0) break
                digest.update(buffer, 0, read)
            }
        }
        val actualHex = digest.digest().joinToString("") { "%02x".format(it) }
        return actualHex.equals(expectedHex.trim(), ignoreCase = true)
    }

    /** Launches the system package installer via a FileProvider content:// URI. */
    private fun launchInstaller(apk: File) {
        val authority = "${context.packageName}.fileprovider"
        val uri = FileProvider.getUriForFile(context, authority, apk)

        val intent = Intent(Intent.ACTION_VIEW).apply {
            setDataAndType(uri, "application/vnd.android.package-archive")
            addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
            // We may be starting this from a non-Activity context.
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        }
        context.startActivity(intent)
    }

    private companion object {
        const val POLL_INTERVAL_MS = 400L
    }
}
