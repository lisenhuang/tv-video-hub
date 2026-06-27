package com.tvvideohub.tv.update

import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import com.tvvideohub.tv.ui.MainActivity

/**
 * Relaunches the app right after a self-update is installed. The system delivers
 * [Intent.ACTION_MY_PACKAGE_REPLACED] to this app once its own APK has been replaced, so we use
 * it to bring the user straight back into [MainActivity] instead of leaving them on the
 * installer's "Done" screen.
 *
 * Best-effort: on Android 10+ the OS restricts starting an activity from the background, so on
 * some devices this auto-relaunch is suppressed and the user taps the installer's "Open" once
 * instead. Android TV and older releases generally allow it. Requires no extra permission, and
 * the broadcast is a protected one only the system can send.
 */
class RestartOnUpdateReceiver : BroadcastReceiver() {
    override fun onReceive(context: Context, intent: Intent) {
        if (intent.action != Intent.ACTION_MY_PACKAGE_REPLACED) return
        val launch = Intent(context, MainActivity::class.java).apply {
            addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
        }
        // May be blocked by background-activity-launch limits on some OS versions — best effort.
        runCatching { context.startActivity(launch) }
    }
}
