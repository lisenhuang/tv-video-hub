package com.tvvideohub.tv.ui

import android.content.Context
import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.compose.runtime.getValue
import androidx.compose.ui.res.stringResource
import androidx.lifecycle.ViewModelProvider
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.tvvideohub.tv.BuildConfig
import com.tvvideohub.tv.R
import com.tvvideohub.tv.core.LocaleHelper
import com.tvvideohub.tv.core.SettingsStore

/**
 * Root activity. On every launch it evaluates [RootViewModel] to decide whether to show
 * first-run setup, a "reconfigure the backend URL" prompt, an offline screen, or the
 * normal catalog. Declared with both LAUNCHER and LEANBACK_LAUNCHER so the one APK runs
 * on phones and on Android TV. Wraps everything in the user-selected light/dark theme.
 */
class MainActivity : ComponentActivity() {

    private lateinit var vm: RootViewModel

    override fun attachBaseContext(newBase: Context) {
        super.attachBaseContext(LocaleHelper.wrap(newBase, SettingsStore.get(newBase).language.value))
    }

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        vm = ViewModelProvider(
            this, ViewModelProvider.AndroidViewModelFactory.getInstance(application)
        )[RootViewModel::class.java]

        setContent {
            val themeMode by vm.themeMode.collectAsStateWithLifecycle()
            val state by vm.state.collectAsStateWithLifecycle()

            TvVideoHubTheme(themeMode = themeMode) {
                when (val s = state) {
                    is RootState.Loading -> SplashScreen()

                    is RootState.NeedsSetup -> BaseUrlScreen(
                        title = stringResource(R.string.setup_title),
                        subtitle = stringResource(R.string.setup_subtitle),
                        initialUrl = vm.currentBaseUrl ?: BuildConfig.BACKEND_BASE_URL,
                        onTest = vm::test,
                        onSave = vm::saveBaseUrl,
                        onOpenDownloads = { open(DownloadsActivity::class.java) },
                    )

                    is RootState.Reconfigure -> BaseUrlScreen(
                        title = stringResource(R.string.reconfigure_title),
                        subtitle = stringResource(R.string.reconfigure_subtitle, s.attemptedUrl),
                        initialUrl = s.attemptedUrl,
                        onTest = vm::test,
                        onSave = vm::saveBaseUrl,
                        onOpenDownloads = { open(DownloadsActivity::class.java) },
                    )

                    is RootState.OfflineNoBackend -> OfflineScreen(
                        onOpenDownloads = { open(DownloadsActivity::class.java) },
                        onRetry = vm::refresh,
                    )

                    is RootState.NeedsAccessCode -> AccessCodeScreen(
                        onSubmit = vm::submitAccessCode,
                        onOpenDownloads = { open(DownloadsActivity::class.java) },
                    )

                    is RootState.Ready -> CatalogRoute(
                        onVideoSelected = { id -> startActivity(DetailActivity.intent(this, id)) },
                        onOpenDownloads = { open(DownloadsActivity::class.java) },
                        onOpenSettings = { open(SettingsActivity::class.java) },
                        onOpenInstallSettings = { intent: Intent -> startActivity(intent) },
                    )
                }
            }
        }
    }

    override fun onResume() {
        super.onResume()
        // Re-evaluate when returning (e.g. after changing the URL/theme in Settings, or
        // when connectivity changed). Cheap; resolves back to Ready quickly when fine.
        vm.refresh()
    }

    private fun open(cls: Class<*>) = startActivity(Intent(this, cls))
}
