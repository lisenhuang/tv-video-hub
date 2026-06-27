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
    /** Backend is up but requires an access code the app doesn't have (or has an invalid one). */
    data object NeedsAccessCode : RootState
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
            ApiClient.setAccessCode(settings.accessCode.value)

            val reachable = repository.isBackendReachable()
            _state.value = when {
                reachable -> if (accessGateBlocks()) RootState.NeedsAccessCode else RootState.Ready
                getApplication<Application>().hasInternet() -> RootState.Reconfigure(url)
                else -> RootState.OfflineNoBackend
            }
        }
    }

    /**
     * True when the backend requires an access code and the stored one is missing/invalid. On any
     * error (e.g. a transient blip) returns false so we don't lock the user out — the content
     * endpoints are still gated server-side, so this can only ever be over-permissive client-side.
     */
    private suspend fun accessGateBlocks(): Boolean {
        val status = runCatching { repository.getAccessStatus() }.getOrNull() ?: return false
        return status.required && !status.valid
    }

    /**
     * Save + apply a typed access code and re-check. Returns true if it unlocked content (state
     * moves to [RootState.Ready]); false leaves the gate up so the screen can show an error.
     */
    suspend fun submitAccessCode(code: String): Boolean {
        settings.setAccessCode(code)
        ApiClient.setAccessCode(settings.accessCode.value)
        val ok = !accessGateBlocks()
        if (ok) _state.value = RootState.Ready
        return ok
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
