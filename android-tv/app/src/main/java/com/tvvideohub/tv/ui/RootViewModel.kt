package com.tvvideohub.tv.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tvvideohub.tv.core.SettingsStore
import com.tvvideohub.tv.core.ThemeMode
import com.tvvideohub.tv.core.hasInternet
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.data.api.ApiClient
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.launch

/**
 * Drives the top-level app gate evaluated on every launch:
 *  - no base URL yet            -> [RootState.NeedsSetup] (first-run config)
 *  - online but URL unreachable -> [RootState.Reconfigure] (ask user to fix the URL)
 *  - offline (no internet)      -> [RootState.OfflineNoBackend] (offer downloaded videos)
 *  - URL reachable              -> [RootState.Ready] (normal catalog)
 */
sealed interface RootState {
    data object Loading : RootState
    data object NeedsSetup : RootState
    data class Reconfigure(val attemptedUrl: String) : RootState
    data object OfflineNoBackend : RootState
    data object Ready : RootState
}

class RootViewModel(application: Application) : AndroidViewModel(application) {

    private val settings = SettingsStore.get(application)
    private val repository = CatalogRepository()

    val themeMode: StateFlow<ThemeMode> = settings.themeMode

    private val _state = MutableStateFlow<RootState>(RootState.Loading)
    val state: StateFlow<RootState> = _state

    init { refresh() }

    /** Re-evaluate the gate (call on launch, after saving a URL, and on "Retry"). */
    fun refresh() {
        _state.value = RootState.Loading
        viewModelScope.launch {
            val url = settings.baseUrl.value
            if (url.isNullOrBlank()) {
                _state.value = RootState.NeedsSetup
                return@launch
            }
            ApiClient.configure(url)

            val reachable = repository.isBackendReachable()
            _state.value = when {
                reachable -> RootState.Ready
                getApplication<Application>().hasInternet() -> RootState.Reconfigure(url)
                else -> RootState.OfflineNoBackend
            }
        }
    }

    /** Health-check a candidate URL without persisting it (for the "Test" button). */
    suspend fun test(url: String): Boolean {
        ApiClient.configure(url)
        return repository.isBackendReachable()
    }

    /** Persist a new base URL and re-evaluate. */
    fun saveBaseUrl(url: String) {
        settings.setBaseUrl(url)
        ApiClient.configure(settings.baseUrl.value)
        refresh()
    }

    fun setThemeMode(mode: ThemeMode) = settings.setThemeMode(mode)

    val currentBaseUrl: String? get() = settings.baseUrl.value
}
