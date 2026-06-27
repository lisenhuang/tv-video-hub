@file:OptIn(androidx.tv.material3.ExperimentalTvMaterial3Api::class)

package com.tvvideohub.tv.ui

import com.tvvideohub.tv.ui.components.AppButton

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
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.input.ImeAction
import androidx.compose.ui.text.input.KeyboardType
import androidx.compose.ui.unit.dp
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import com.tvvideohub.tv.R
import com.tvvideohub.tv.ui.components.OutlinedInput
import kotlinx.coroutines.launch

@Composable
fun SplashScreen() {
    Box(Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Text(
            text = stringResource(R.string.app_name),
            style = MaterialTheme.typography.headlineSmall,
            color = MaterialTheme.colorScheme.onBackground
        )
    }
}

/**
 * First-run / settings screen for the backend base URL. Lets the user type a URL, test it,
 * and save. Shared by [RootState.NeedsSetup] and [RootState.Reconfigure].
 */
@Composable
fun BaseUrlScreen(
    title: String,
    subtitle: String,
    initialUrl: String,
    onTest: suspend (String) -> Boolean,
    onSave: (String) -> Unit,
    onOpenDownloads: (() -> Unit)? = null,
) {
    var url by remember { mutableStateOf(initialUrl) }
    var testResult by remember { mutableStateOf<Boolean?>(null) }
    var testing by remember { mutableStateOf(false) }
    val scope = rememberCoroutineScope()
    val colors = MaterialTheme.colorScheme

    Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
        Column(
            modifier = Modifier.widthIn(max = 560.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            Text(title, style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)
            Text(
                subtitle,
                style = MaterialTheme.typography.bodyMedium,
                color = colors.onSurface,
                modifier = Modifier.padding(top = 8.dp, bottom = 24.dp)
            )

            OutlinedInput(
                value = url,
                onValueChange = { url = it; testResult = null },
                label = stringResource(R.string.setup_base_url_label),
                keyboardType = KeyboardType.Uri,
                imeAction = ImeAction.Done,
                modifier = Modifier.fillMaxWidth()
            )

            testResult?.let { ok ->
                Text(
                    text = stringResource(if (ok) R.string.setup_reachable else R.string.setup_not_reachable),
                    style = MaterialTheme.typography.bodyMedium,
                    color = if (ok) colors.primary else colors.onSurface,
                    modifier = Modifier.padding(top = 12.dp)
                )
            }

            Row(
                modifier = Modifier.padding(top = 24.dp),
                horizontalArrangement = Arrangement.spacedBy(16.dp)
            ) {
                AppButton(
                    onClick = {
                        if (url.isBlank() || testing) return@AppButton
                        testing = true
                        scope.launch {
                            testResult = runCatching { onTest(url) }.getOrDefault(false)
                            testing = false
                        }
                    }
                ) { Text(stringResource(if (testing) R.string.action_testing else R.string.action_test)) }

                AppButton(onClick = {
                    // Validate it's a real tv-video-hub backend before saving — reject random URLs.
                    if (url.isBlank() || testing) return@AppButton
                    testing = true
                    scope.launch {
                        val ok = runCatching { onTest(url) }.getOrDefault(false)
                        testing = false
                        testResult = ok
                        if (ok) onSave(url)
                    }
                }) {
                    Text(stringResource(R.string.action_save))
                }

                if (onOpenDownloads != null) {
                    AppButton(onClick = onOpenDownloads) { Text(stringResource(R.string.action_open_downloads)) }
                }
            }
        }
    }
}

/** Shown when there's no internet at all: the catalog is unavailable but downloads play. */
@Composable
fun OfflineScreen(onOpenDownloads: () -> Unit, onRetry: () -> Unit) {
    val colors = MaterialTheme.colorScheme
    Box(Modifier.fillMaxSize().padding(32.dp), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(stringResource(R.string.offline_title), style = MaterialTheme.typography.headlineMedium, color = colors.onBackground)
            Text(
                stringResource(R.string.offline_subtitle),
                style = MaterialTheme.typography.bodyMedium,
                color = colors.onSurface,
                modifier = Modifier.padding(top = 8.dp, bottom = 24.dp)
            )
            Row(horizontalArrangement = Arrangement.spacedBy(16.dp)) {
                AppButton(onClick = onOpenDownloads) { Text(stringResource(R.string.offline_action_view_downloads)) }
                AppButton(onClick = onRetry) { Text(stringResource(R.string.action_retry)) }
            }
        }
    }
}
