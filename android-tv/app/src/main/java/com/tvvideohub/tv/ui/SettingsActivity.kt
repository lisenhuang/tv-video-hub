@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.rememberCoroutineScope
import androidx.compose.runtime.setValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.tvvideohub.tv.R
import com.tvvideohub.tv.core.AppLanguage
import com.tvvideohub.tv.core.LocaleHelper
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.core.ThemeMode
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.data.api.ApiClient
import com.tvvideohub.tv.ui.components.OutlinedInput
import com.tvvideohub.tv.ui.components.ParentalGate
import androidx.tv.material3.Button
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import kotlinx.coroutines.launch

class SettingsActivity : ComponentActivity() {
    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, SettingsStore.get(newBase).language.value))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent {
            AppTheme {
                SettingsScreen(
                    onBack = { finish() },
                    // Re-create so attachBaseContext re-wraps with the new locale immediately.
                    onLanguageChanged = { recreate() },
                )
            }
        }
    }
}

@Composable
private fun SettingsScreen(onBack: () -> Unit, onLanguageChanged: () -> Unit) {
    val context = LocalContext.current
    val settings = remember { SettingsStore.get(context) }
    val repo = remember { CatalogRepository() }
    val scope = rememberCoroutineScope()
    val colors = MaterialTheme.colorScheme

    val themeMode by settings.themeMode.collectAsStateWithLifecycle()
    val language by settings.language.collectAsStateWithLifecycle()
    var url by remember { mutableStateOf(settings.baseUrl.value ?: "") }
    var status by remember { mutableStateOf<String?>(null) }
    var showGate by remember { mutableStateOf(false) }

    val savedText = stringResource(R.string.settings_saved)
    val reachableText = stringResource(R.string.settings_reachable)
    val notReachableText = stringResource(R.string.settings_not_reachable)

    fun commitUrl() {
        // Validate it's a real tv-video-hub backend before persisting; revert otherwise.
        scope.launch {
            ApiClient.configure(url)
            if (repo.isBackendReachable()) {
                settings.setBaseUrl(url)
                ApiClient.configure(settings.baseUrl.value)
                status = savedText
            } else {
                ApiClient.configure(settings.baseUrl.value)
                status = notReachableText
            }
        }
    }

    Box(Modifier.fillMaxSize()) {
        Column(Modifier.padding(40.dp).widthIn(max = 640.dp)) {
            Text(stringResource(R.string.settings_title), style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)

            // --- Backend base URL ---
            Text(
                stringResource(R.string.settings_backend_heading),
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 24.dp, bottom = 8.dp)
            )
            OutlinedInput(
                value = url,
                onValueChange = { url = it; status = null },
                label = stringResource(R.string.settings_base_url_label),
                keyboardType = KeyboardType.Uri,
                imeAction = ImeAction.Done,
                modifier = Modifier.fillMaxWidth()
            )
            status?.let {
                Text(it, style = MaterialTheme.typography.bodyMedium, color = colors.onSurface, modifier = Modifier.padding(top = 8.dp))
            }
            Row(modifier = Modifier.padding(top = 12.dp), horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                Button(onClick = {
                    if (url.isBlank()) return@Button
                    scope.launch {
                        ApiClient.configure(url)
                        status = if (repo.isBackendReachable()) reachableText else notReachableText
                    }
                }) { Text(stringResource(R.string.action_test)) }
                Button(onClick = {
                    if (url.isBlank()) return@Button
                    // Gate a change to an already-configured URL behind a math quiz so
                    // kids can't alter it; saving the same value is a no-op anyway.
                    if (url == settings.baseUrl.value) { commitUrl(); return@Button }
                    showGate = true
                }) { Text(stringResource(R.string.action_save)) }
            }

            // --- Theme ---
            Text(
                stringResource(R.string.settings_appearance_heading),
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 28.dp, bottom = 8.dp)
            )
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                ThemeMode.entries.forEach { mode ->
                    val selected = mode == themeMode
                    Button(onClick = { settings.setThemeMode(mode) }) {
                        Text((if (selected) "● " else "") + stringResource(mode.labelRes()))
                    }
                }
            }

            // --- Language ---
            Text(
                stringResource(R.string.settings_language_heading),
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 28.dp, bottom = 8.dp)
            )
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                AppLanguage.entries.forEach { lang ->
                    val selected = lang == language
                    Button(onClick = {
                        if (lang != language) {
                            settings.setLanguage(lang)
                            onLanguageChanged()
                        }
                    }) {
                        Text((if (selected) "● " else "") + stringResource(lang.labelRes()))
                    }
                }
            }

            // --- Storage ---
            Text(
                stringResource(R.string.settings_storage_heading),
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 28.dp, bottom = 8.dp)
            )
            Button(onClick = { context.startActivity(Intent(context, StorageActivity::class.java)) }) {
                Text(stringResource(R.string.settings_manage_storage))
            }

            Button(onClick = onBack, modifier = Modifier.padding(top = 32.dp)) { Text(stringResource(R.string.action_back)) }
        }

        if (showGate) {
            ParentalGate(
                prompt = stringResource(R.string.settings_gate_change_backend),
                onPass = { showGate = false; commitUrl() },
                onCancel = { showGate = false }
            )
        }
    }
}

private fun ThemeMode.labelRes(): Int = when (this) {
    ThemeMode.SYSTEM -> R.string.settings_theme_system
    ThemeMode.LIGHT -> R.string.settings_theme_light
    ThemeMode.DARK -> R.string.settings_theme_dark
}

private fun AppLanguage.labelRes(): Int = when (this) {
    AppLanguage.SYSTEM -> R.string.settings_language_system
    AppLanguage.ENGLISH -> R.string.settings_language_english
    AppLanguage.CHINESE -> R.string.settings_language_chinese
}
