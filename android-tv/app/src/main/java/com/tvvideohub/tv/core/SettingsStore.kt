package com.tvvideohub.tv.core

import android.content.Context
import android.content.SharedPreferences
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow

/** User-selectable theme. Default [SYSTEM] follows the OS light/dark setting. */
enum class ThemeMode { SYSTEM, LIGHT, DARK }

/**
 * On-device settings, backed by SharedPreferences. Holds the **backend base URL** (set on
 * first run, changeable later) and the **theme mode**.
 *
 * Backward-compat (CLAUDE.md): these preference keys are an on-device contract — never
 * rename or repurpose them, or an app upgrade would lose the user's configured URL/theme.
 * Only add new keys.
 */
class SettingsStore private constructor(private val prefs: SharedPreferences) {

    private val _baseUrl = MutableStateFlow(prefs.getString(KEY_BASE_URL, null))
    val baseUrl: StateFlow<String?> = _baseUrl

    private val _themeMode = MutableStateFlow(readThemeMode())
    val themeMode: StateFlow<ThemeMode> = _themeMode

    val isConfigured: Boolean get() = !_baseUrl.value.isNullOrBlank()

    fun setBaseUrl(url: String) {
        val normalized = normalize(url)
        prefs.edit().putString(KEY_BASE_URL, normalized).apply()
        _baseUrl.value = normalized
    }

    fun setThemeMode(mode: ThemeMode) {
        prefs.edit().putString(KEY_THEME, mode.name).apply()
        _themeMode.value = mode
    }

    private fun readThemeMode(): ThemeMode =
        runCatching { ThemeMode.valueOf(prefs.getString(KEY_THEME, ThemeMode.SYSTEM.name)!!) }
            .getOrDefault(ThemeMode.SYSTEM)

    companion object {
        private const val PREFS_NAME = "tv_video_hub_settings"
        private const val KEY_BASE_URL = "backend_base_url"
        private const val KEY_THEME = "theme_mode"

        @Volatile private var instance: SettingsStore? = null

        fun get(context: Context): SettingsStore =
            instance ?: synchronized(this) {
                instance ?: SettingsStore(
                    context.applicationContext.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE)
                ).also { instance = it }
            }

        /** Ensure a trailing slash (Retrofit requires it) and a scheme. */
        fun normalize(url: String): String {
            var u = url.trim()
            if (u.isEmpty()) return u
            if (!u.startsWith("http://") && !u.startsWith("https://")) u = "https://$u"
            if (!u.endsWith("/")) u = "$u/"
            return u
        }
    }
}
