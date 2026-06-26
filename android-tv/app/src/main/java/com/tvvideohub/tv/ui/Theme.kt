package com.tvvideohub.tv.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.darkColorScheme
import androidx.tv.material3.lightColorScheme
import com.tvvideohub.tv.core.ThemeMode

/**
 * TV Material3 theme with light AND dark palettes. By default it follows the system
 * setting ([ThemeMode.SYSTEM]); the user can force light/dark from Settings. tv-material's
 * MaterialTheme renders well on both phones and TVs, so one theme covers both form factors.
 */

private val Brand = Color(0xFF3D7BFF)

private val DarkColors = darkColorScheme(
    primary = Brand,
    onPrimary = Color(0xFFFFFFFF),
    surface = Color(0xFF11151C),
    onSurface = Color(0xFFE6EAF2),
    surfaceVariant = Color(0xFF1B212B),
    background = Color(0xFF0A0C10),
    onBackground = Color(0xFFE6EAF2),
    border = Color(0xFF2A3340)
)

private val LightColors = lightColorScheme(
    primary = Brand,
    onPrimary = Color(0xFFFFFFFF),
    surface = Color(0xFFFFFFFF),
    onSurface = Color(0xFF11151C),
    surfaceVariant = Color(0xFFEDF1F7),
    background = Color(0xFFF6F8FB),
    onBackground = Color(0xFF11151C),
    border = Color(0xFFD4DBE6)
)

@Composable
fun resolveDark(mode: ThemeMode): Boolean = when (mode) {
    ThemeMode.SYSTEM -> isSystemInDarkTheme()
    ThemeMode.LIGHT -> false
    ThemeMode.DARK -> true
}

@Composable
fun TvVideoHubTheme(
    themeMode: ThemeMode = ThemeMode.SYSTEM,
    content: @Composable () -> Unit
) {
    val dark = resolveDark(themeMode)
    MaterialTheme(
        colorScheme = if (dark) DarkColors else LightColors,
        content = content
    )
}
