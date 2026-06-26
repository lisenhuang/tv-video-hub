package com.tvvideohub.tv.ui

import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.platform.LocalContext
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.tvvideohub.tv.core.SettingsStore

/**
 * Wraps content in the app theme, reading the user's persisted theme mode so every
 * activity (Detail, Downloads, Settings) honors the same light/dark/system choice live.
 */
@Composable
fun AppTheme(content: @Composable () -> Unit) {
    val context = LocalContext.current
    val mode by SettingsStore.get(context).themeMode.collectAsStateWithLifecycle()
    TvVideoHubTheme(themeMode = mode, content = content)
}
