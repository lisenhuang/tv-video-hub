# android-tv

The client half of **tv-video-hub**: a Kotlin app that browses and plays videos from the
backend, **caches/downloads them for offline playback**, and **self-updates** by checking
the backend's release endpoint.

It is built with **Compose for TV** (`androidx.tv`) + **Media3/ExoPlayer**, and ships as a
**single APK that installs and runs on both Android TVs and phones/tablets** (see
[Form factors](#form-factors-phone--tv)).

> No Android SDK is required to read or review this source. Building requires the SDK and
> is done in CI (Gradle 8.7, JDK 21, AGP 8.7.x, Kotlin 2.0.x).

---

## What it does

1. **First-run setup** — on first launch (or whenever no backend is configured) the app
   asks for the **backend base URL**, with a *Test* button to verify it before saving. The
   URL is stored on-device and can be changed later in **Settings**.
2. **Connectivity gate on every launch** — if the saved URL is reachable, the catalog
   shows. If you're **online but the URL doesn't respond**, a *"Can't reach the backend"*
   screen asks you to re-enter it. If you're **offline**, it offers your downloaded videos.
3. **Self-update on launch** — calls `GET /api/app/latest`, compares `versionCode` to its
   own `BuildConfig.VERSION_CODE`, and if newer, downloads the APK, verifies its
   **SHA-256**, and launches the installer (see [Self-update](#self-update)).
4. **Browse** — `GET /api/videos` populates a responsive grid; downloaded items show an
   *Offline* badge.
5. **Play, cache & download** — opening a video shows a detail screen with **Play** and
   **Download**. Streaming fills an on-device cache (instant re-watch / scrubbing); a full
   **Download** makes the video playable **offline**, with progress and a *Remove* action.
6. **Theme** — light/dark, following the system by default with a manual override.

The HTTP/JSON contract lives in the repo root `README.md`; the DTOs in
`app/src/main/java/com/tvvideohub/tv/data/dto/` mirror it field-for-field.

---

## Configure the backend URL

The base URL is a **runtime setting**, not a hardcoded constant:

- **In the app** — entered on the first-run setup screen and editable later under
  **Settings → Backend**. Stored in `SharedPreferences` (`SettingsStore`) and applied live
  (`ApiClient` rebuilds its Retrofit instance on change). It survives app upgrades.
  Changing an already-configured URL is **gated by a parental math quiz** (a two-digit ×
  one-digit problem, e.g. `17 × 7`) so children can't alter it; first-run setup isn't gated.
- **Build-time default (optional)** — `app/build.gradle.kts` reads the `backendBaseUrl`
  Gradle property into `BuildConfig.BACKEND_BASE_URL`, used only to **pre-fill** the setup
  field. Pass it to seed installs:

  ```sh
  gradle :app:assembleRelease -PbackendBaseUrl=https://media.your-host.example
  ```

  Without it, the field is pre-filled with the placeholder `https://media.example.com`.

---

## Caching & offline

Powered by Media3's cache + download stack (`download/DownloadUtil.kt`):

- **One shared `SimpleCache`** backs both streaming and downloads (8 GB LRU budget).
  Watching a video caches the bytes it streams; an explicit **Download** fetches the whole
  file via a `DownloadManager` foreground service (`MediaDownloadService`).
- **Keyed by stable video id** (`customCacheKey`), *not* the playback URL. The backend
  hands out short-lived **presigned** URLs that rotate, so URL-based keys would orphan
  cached bytes on every refresh and on every app upgrade. Id-based keys keep cache and
  downloads valid across both. **(See `CLAUDE.md` — don't change this.)**
- **Offline playback** — a completed download plays straight from the cache with no
  network. Downloaded items are listed in **Downloads**, which works fully offline (it
  reads the local download index; metadata for title/thumbnail is stashed in the
  `DownloadRequest`). The player also falls back to the cache automatically if a fetch
  fails but the video was downloaded.

---

## Theming

`Theme.kt` defines light **and** dark `tv-material3` color schemes. `ThemeMode`
(`SYSTEM`/`LIGHT`/`DARK`) is persisted in `SettingsStore`; **System** (the default) follows
`isSystemInDarkTheme()`. Change it under **Settings → Appearance**; every screen re-themes
live via the shared `AppTheme`/`TvVideoHubTheme` host.

---

## Form factors (phone + TV)

The same APK targets **both** TVs and phones/tablets:

- **Dual launcher** — `MainActivity` declares both `LAUNCHER` (phone app drawer) and
  `LEANBACK_LAUNCHER` (TV home), with both an `android:icon` and an `android:banner`.
- **`required="false"` feature flags** — `android.software.leanback` and
  `android.hardware.touchscreen` are both `required="false"`, so neither form factor is
  excluded at install time.
- **Input parity** — focusable/clickable tv-material `Card`s with obvious focus visuals and
  initial focus; `onClick` fires for both DPAD-center and touch; the player maps
  DPAD-center → play/pause, ←/→ → seek, BACK → exit while keeping on-screen touch controls.
  Text entry (setup/settings) uses a focusable `BasicTextField` that brings up the IME on
  both TV and phone.
- **Responsive grid** — column count adapts to screen width (≈2 on a phone up to 6 on a TV).

---

## Self-update

On launch `CatalogViewModel` calls `GET /api/app/latest`. If `versionCode` exceeds the
installed `BuildConfig.VERSION_CODE`, a prompt offers to update. `UpdateManager` then
downloads via `DownloadManager` into a `FileProvider`-exposed cache dir, verifies the
**SHA-256** against `latest.sha256`, and launches the installer.

### ⚠️ "Install unknown apps" permission caveat

`REQUEST_INSTALL_PACKAGES` is necessary but **not sufficient**: on **API 26+** the user
must also grant this app the *"install unknown apps"* capability. The UI detects this
(`canRequestInstalls()`), shows an "Allow installs" panel, and deep-links to
`Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES`. On managed TV fleets, prefer a privileged /
device-owner installer. Self-update via sideload-style install is for **internal/private
distribution** (which this project is); for public Play distribution, ship via Play.

### Signing for self-update

Android only updates an app when the new APK is signed with the **same** key. CI signs
release builds with a keystore from secrets (`ANDROID_KEYSTORE_BASE64`, etc.); see
`app/build.gradle.kts` (`signingConfigs`) and `.github/workflows/android-build.yml`. Local
release builds without a keystore fall back to the debug key.

---

## Build & sideload

```sh
cd android-tv
gradle wrapper --gradle-version 8.7     # creates ./gradlew + gradle-wrapper.jar (not committed)
./gradlew :app:assembleDebug
./gradlew :app:assembleRelease -PbackendBaseUrl=https://media.your-host.example
adb install -r app/build/outputs/apk/release/app-release.apk
```

The Gradle wrapper jar/scripts are intentionally not committed (binary artifacts); CI
generates them (or uses the system `gradle`) before building.

---

## Project layout

```
app/src/main/java/com/tvvideohub/tv/
├── MediaHubApp.kt                  # configures ApiClient + inits downloads on startup
├── core/
│   ├── SettingsStore.kt            # persisted base URL + theme mode (SharedPreferences)
│   └── Connectivity.kt             # hasInternet()
├── data/
│   ├── dto/Dtos.kt                 # contract DTOs
│   ├── api/MediaHubApi.kt          # Retrofit interface (+ health)
│   ├── api/ApiClient.kt            # runtime-reconfigurable Retrofit client
│   └── CatalogRepository.kt
├── ui/
│   ├── MainActivity.kt             # root gate: setup / reconfigure / offline / catalog
│   ├── RootViewModel.kt            # launch-time connectivity state machine
│   ├── RootScreens.kt              # setup / reconfigure / offline screens
│   ├── CatalogViewModel.kt
│   ├── CatalogScreen.kt            # grid + header (Downloads/Settings) + offline badge
│   ├── CatalogComponents.kt        # update prompt, error state
│   ├── DetailActivity.kt           # play + download/remove + progress
│   ├── DownloadsActivity.kt        # offline list (works with no network)
│   ├── SettingsActivity.kt         # change base URL + theme
│   ├── Theme.kt / ThemeHost.kt     # light/dark tv-material3 theme
│   └── components/Inputs.kt        # focusable text field for TV+touch
├── player/PlayerActivity.kt        # ExoPlayer via shared cache; online or offline
├── download/
│   ├── DownloadUtil.kt             # SimpleCache + DownloadManager + cache data source
│   ├── MediaDownloadService.kt     # foreground download service
│   └── DownloadModels.kt           # offline metadata + UI projection
└── update/UpdateManager.kt         # self-update (download, verify SHA-256, install)
```
