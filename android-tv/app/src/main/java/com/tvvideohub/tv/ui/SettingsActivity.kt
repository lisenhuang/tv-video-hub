@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

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
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
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
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContent { AppTheme { SettingsScreen(onBack = { finish() }) } }
    }
}

@Composable
private fun SettingsScreen(onBack: () -> Unit) {
    val context = LocalContext.current
    val settings = remember { SettingsStore.get(context) }
    val repo = remember { CatalogRepository() }
    val scope = rememberCoroutineScope()
    val colors = MaterialTheme.colorScheme

    val themeMode by settings.themeMode.collectAsStateWithLifecycle()
    var url by remember { mutableStateOf(settings.baseUrl.value ?: "") }
    var status by remember { mutableStateOf<String?>(null) }
    var showGate by remember { mutableStateOf(false) }

    fun commitUrl() {
        settings.setBaseUrl(url)
        ApiClient.configure(settings.baseUrl.value)
        status = "Saved"
    }

    Box(Modifier.fillMaxSize()) {
        Column(Modifier.padding(40.dp).widthIn(max = 640.dp)) {
            Text("Settings", style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)

            // --- Backend base URL ---
            Text(
                "Backend",
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 24.dp, bottom = 8.dp)
            )
            OutlinedInput(
                value = url,
                onValueChange = { url = it; status = null },
                label = "Base URL",
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
                        status = if (repo.isBackendReachable()) "✓ Reachable" else "✗ Not reachable"
                    }
                }) { Text("Test") }
                Button(onClick = {
                    if (url.isBlank()) return@Button
                    // Gate a change to an already-configured URL behind a math quiz so
                    // kids can't alter it; saving the same value is a no-op anyway.
                    if (url == settings.baseUrl.value) { commitUrl(); return@Button }
                    showGate = true
                }) { Text("Save") }
            }

            // --- Theme ---
            Text(
                "Appearance",
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 28.dp, bottom = 8.dp)
            )
            Row(horizontalArrangement = Arrangement.spacedBy(12.dp)) {
                ThemeMode.entries.forEach { mode ->
                    val selected = mode == themeMode
                    Button(onClick = { settings.setThemeMode(mode) }) {
                        Text((if (selected) "● " else "") + mode.label())
                    }
                }
            }

            // --- Storage ---
            Text(
                "Storage",
                style = MaterialTheme.typography.titleMedium,
                color = colors.onBackground,
                modifier = Modifier.padding(top = 28.dp, bottom = 8.dp)
            )
            Button(onClick = { context.startActivity(Intent(context, StorageActivity::class.java)) }) {
                Text("Manage storage & cache")
            }

            Button(onClick = onBack, modifier = Modifier.padding(top = 32.dp)) { Text("Back") }
        }

        if (showGate) {
            ParentalGate(
                prompt = "Solve to change the backend address",
                onPass = { showGate = false; commitUrl() },
                onCancel = { showGate = false }
            )
        }
    }
}

private fun ThemeMode.label(): String = when (this) {
    ThemeMode.SYSTEM -> "System"
    ThemeMode.LIGHT -> "Light"
    ThemeMode.DARK -> "Dark"
}
