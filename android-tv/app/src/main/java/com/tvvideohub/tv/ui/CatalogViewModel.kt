package com.tvvideohub.tv.ui

import android.app.Application
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import com.tvvideohub.tv.R
import com.tvvideohub.tv.data.CatalogRepository
import com.tvvideohub.tv.data.dto.AppRelease
import com.tvvideohub.tv.data.dto.VideoSummary
import com.tvvideohub.tv.update.UpdateManager
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch

/** UI state for the catalog screen. */
sealed interface CatalogUiState {
    data object Loading : CatalogUiState
    data class Content(val videos: List<VideoSummary>) : CatalogUiState
    data object Empty : CatalogUiState
    data class Error(val message: String) : CatalogUiState
}

/** State of the self-update flow, surfaced to the UI as a dialog/snackbar. */
sealed interface UpdateUiState {
    data object Idle : UpdateUiState
    /** A newer release is available; offer it to the user. */
    data class Available(val release: AppRelease) : UpdateUiState
    data object Downloading : UpdateUiState
    /** The app must be granted "install unknown apps" before it can self-update. */
    data class NeedsPermission(val release: AppRelease) : UpdateUiState
    data object InstallLaunched : UpdateUiState
    data class Failed(val message: String) : UpdateUiState
}

class CatalogViewModel(
    application: Application
) : AndroidViewModel(application) {

    // Built internally so the stock AndroidViewModelFactory (which looks for an
    // (Application) constructor) can instantiate this ViewModel without a custom factory.
    private val repository: CatalogRepository = CatalogRepository()
    private val updateManager: UpdateManager = UpdateManager(application)

    private val _uiState = MutableStateFlow<CatalogUiState>(CatalogUiState.Loading)
    val uiState: StateFlow<CatalogUiState> = _uiState.asStateFlow()

    private val _updateState = MutableStateFlow<UpdateUiState>(UpdateUiState.Idle)
    val updateState: StateFlow<UpdateUiState> = _updateState.asStateFlow()

    init {
        loadVideos()
        checkForUpdate()
    }

    fun loadVideos() {
        _uiState.value = CatalogUiState.Loading
        viewModelScope.launch {
            try {
                val videos = repository.listVideos()
                _uiState.value =
                    if (videos.isEmpty()) CatalogUiState.Empty
                    else CatalogUiState.Content(videos)
            } catch (t: Throwable) {
                _uiState.value = CatalogUiState.Error(
                    t.message ?: getApplication<Application>().getString(R.string.catalog_error_unknown)
                )
            }
        }
    }

    /** Runs on launch: ask the backend for the latest release and offer it if newer. */
    fun checkForUpdate() {
        viewModelScope.launch {
            try {
                val latest = repository.getLatestRelease()
                val newer = updateManager.isNewer(latest)
                if (newer != null) {
                    _updateState.value = UpdateUiState.Available(newer)
                }
            } catch (_: Throwable) {
                // Update check is best-effort; never block the catalog on it.
            }
        }
    }

    /** User accepted the update: download, verify, and launch the installer. */
    fun startUpdate(release: AppRelease) {
        _updateState.value = UpdateUiState.Downloading
        viewModelScope.launch {
            when (val result = updateManager.downloadAndInstall(release)) {
                is UpdateManager.Result.InstallLaunched ->
                    _updateState.value = UpdateUiState.InstallLaunched
                is UpdateManager.Result.NeedsInstallPermission ->
                    _updateState.value = UpdateUiState.NeedsPermission(release)
                is UpdateManager.Result.Failed ->
                    _updateState.value = UpdateUiState.Failed(result.message)
            }
        }
    }

    /** Dismiss the update prompt (user chose "Later"). */
    fun dismissUpdate() {
        _updateState.update { UpdateUiState.Idle }
    }

    fun unknownSourcesSettingsIntent() = updateManager.unknownSourcesSettingsIntent()

    fun canRequestInstalls(): Boolean = updateManager.canRequestInstalls()
}
