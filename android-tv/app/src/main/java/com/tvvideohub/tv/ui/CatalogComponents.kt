package com.tvvideohub.tv.ui

import com.tvvideohub.tv.ui.components.AppButton
import androidx.activity.compose.BackHandler
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.widthIn
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.runtime.Composable
import androidx.compose.runtime.DisposableEffect
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.remember
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.unit.dp
import androidx.tv.material3.ExperimentalTvMaterial3Api
import androidx.tv.material3.MaterialTheme
import androidx.tv.material3.Text
import com.tvvideohub.tv.R
import com.tvvideohub.tv.data.dto.AppRelease

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun ErrorState(message: String, onRetry: () -> Unit) {
    val retryFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { retryFocus.requestFocus() } }

    Box(modifier = Modifier.fillMaxSize(), contentAlignment = Alignment.Center) {
        Column(horizontalAlignment = Alignment.CenterHorizontally) {
            Text(
                text = stringResource(R.string.catalog_error),
                style = MaterialTheme.typography.titleLarge,
                color = MaterialTheme.colorScheme.onBackground
            )
            Text(
                text = message,
                style = MaterialTheme.typography.bodyMedium,
                color = Color(0xFF8893A7),
                modifier = Modifier.padding(top = 8.dp, bottom = 20.dp)
            )
            AppButton(
                onClick = onRetry,
                modifier = Modifier.focusRequester(retryFocus)
            ) {
                Text(text = stringResource(R.string.action_retry))
            }
        }
    }
}

/**
 * Self-update prompt. Rendered as a centered modal panel (not a system Dialog) so it is
 * naturally focusable by the D-pad and looks correct on TV; touch users can tap it too.
 */
@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
fun UpdateOverlay(
    updateState: UpdateUiState,
    onConfirm: (AppRelease) -> Unit,
    onDismiss: () -> Unit,
    onOpenSettings: () -> Unit
) {
    // Keep the screen awake while the APK is downloading so the Android TV screensaver/daydream
    // doesn't kick in mid-download and interrupt it (mirrors how the player holds the screen on
    // during playback). Scoped to the download only: once it ends, the flag is released so the
    // screensaver can still appear when the device is genuinely idle. Declared before the early
    // return below so the effect's lifecycle is stable across every update state.
    val view = LocalView.current
    val downloading = updateState is UpdateUiState.Downloading
    DisposableEffect(downloading) {
        view.keepScreenOn = downloading
        onDispose { view.keepScreenOn = false }
    }

    if (updateState is UpdateUiState.Idle || updateState is UpdateUiState.InstallLaunched) {
        return
    }

    Box(
        modifier = Modifier
            .fillMaxSize()
            .background(Color(0xCC000000)),
        contentAlignment = Alignment.Center
    ) {
        Column(
            modifier = Modifier
                .widthIn(max = 520.dp)
                .clip(RoundedCornerShape(16.dp))
                .background(MaterialTheme.colorScheme.surface)
                .padding(28.dp),
            horizontalAlignment = Alignment.CenterHorizontally
        ) {
            when (updateState) {
                is UpdateUiState.Available -> AvailablePanel(
                    release = updateState.release,
                    onConfirm = { onConfirm(updateState.release) },
                    onDismiss = onDismiss
                )

                is UpdateUiState.Downloading -> DownloadingPanel(percent = updateState.percent)

                is UpdateUiState.NeedsPermission -> NeedsPermissionPanel(
                    onOpenSettings = onOpenSettings,
                    onDismiss = onDismiss
                )

                is UpdateUiState.Failed -> FailedPanel(
                    message = updateState.message,
                    onDismiss = onDismiss
                )

                // Idle / InstallLaunched handled by the early return above.
                else -> Unit
            }
        }
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun AvailablePanel(
    release: AppRelease,
    onConfirm: () -> Unit,
    onDismiss: () -> Unit
) {
    val updateFocus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { updateFocus.requestFocus() } }

    // Mandatory update: swallow BACK so the prompt can't be dismissed (the "Later" button is
    // hidden below too), forcing the user through the update.
    if (release.forceUpdate) BackHandler(enabled = true) { /* consume — no dismiss */ }

    Text(
        text = stringResource(R.string.update_available_title),
        style = MaterialTheme.typography.headlineSmall,
        color = MaterialTheme.colorScheme.onSurface
    )
    Text(
        text = stringResource(R.string.update_available_message, release.versionName),
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurface,
        modifier = Modifier.padding(top = 12.dp)
    )
    if (release.notes.isNotBlank()) {
        Text(
            text = release.notes,
            style = MaterialTheme.typography.bodyMedium,
            color = Color(0xFF8893A7),
            modifier = Modifier.padding(top = 8.dp)
        )
    }
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 24.dp),
        horizontalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterHorizontally)
    ) {
        AppButton(
            onClick = onConfirm,
            modifier = Modifier.focusRequester(updateFocus)
        ) {
            Text(text = stringResource(R.string.update_action_update))
        }
        // No "Later" for a mandatory update.
        if (!release.forceUpdate) {
            AppButton(onClick = onDismiss) {
                Text(text = stringResource(R.string.update_action_later))
            }
        }
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun NeedsPermissionPanel(
    onOpenSettings: () -> Unit,
    onDismiss: () -> Unit
) {
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }

    Text(
        text = stringResource(R.string.update_install_permission_title),
        style = MaterialTheme.typography.headlineSmall,
        color = MaterialTheme.colorScheme.onSurface
    )
    Text(
        text = stringResource(R.string.update_install_permission_message_choose),
        style = MaterialTheme.typography.bodyLarge,
        color = MaterialTheme.colorScheme.onSurface,
        modifier = Modifier.padding(top = 12.dp)
    )
    Row(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 24.dp),
        horizontalArrangement = Arrangement.spacedBy(16.dp, Alignment.CenterHorizontally)
    ) {
        AppButton(
            onClick = onOpenSettings,
            modifier = Modifier.focusRequester(focus)
        ) {
            Text(text = stringResource(R.string.update_action_open_settings))
        }
        AppButton(onClick = onDismiss) {
            Text(text = stringResource(R.string.update_action_later))
        }
    }
}

@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun FailedPanel(message: String, onDismiss: () -> Unit) {
    val focus = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { focus.requestFocus() } }

    Text(
        text = stringResource(R.string.update_failed_title),
        style = MaterialTheme.typography.headlineSmall,
        color = MaterialTheme.colorScheme.onSurface
    )
    Text(
        text = message,
        style = MaterialTheme.typography.bodyMedium,
        color = Color(0xFF8893A7),
        modifier = Modifier.padding(top = 12.dp, bottom = 24.dp)
    )
    AppButton(
        onClick = onDismiss,
        modifier = Modifier.focusRequester(focus)
    ) {
        Text(text = stringResource(R.string.action_dismiss))
    }
}

/**
 * Download-in-progress panel: title + a determinate progress bar + percentage. [percent] is
 * 0..100, or -1 while the total size is unknown (then we show the generic "please wait" body
 * with the bar empty). The bar is a plain Box (tv-material has no progress indicator).
 */
@OptIn(ExperimentalTvMaterial3Api::class)
@Composable
private fun DownloadingPanel(percent: Int) {
    val known = percent in 0..100
    val fraction = if (known) percent / 100f else 0f

    Text(
        text = stringResource(R.string.update_downloading),
        style = MaterialTheme.typography.headlineSmall,
        color = MaterialTheme.colorScheme.onSurface
    )
    Box(
        modifier = Modifier
            .fillMaxWidth()
            .padding(top = 20.dp)
            .height(8.dp)
            .clip(RoundedCornerShape(4.dp))
            .background(Color(0x33FFFFFF))
    ) {
        if (fraction > 0f) {
            Box(
                modifier = Modifier
                    .fillMaxWidth(fraction)
                    .fillMaxHeight()
                    .clip(RoundedCornerShape(4.dp))
                    .background(MaterialTheme.colorScheme.primary)
            )
        }
    }
    Text(
        text = if (known)
            stringResource(R.string.update_downloading_percent, percent)
        else
            stringResource(R.string.update_downloading_body),
        style = MaterialTheme.typography.bodyMedium,
        color = Color(0xFF8893A7),
        modifier = Modifier.padding(top = 12.dp)
    )
}

