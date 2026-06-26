# 📺 android-tv

Single APK for **Android TV + phones/tablets**. Browse & play backend videos, cache/download
for offline, and self-update. Kotlin · Compose for TV · Media3/ExoPlayer.

> 🛠️ No Android SDK needed to read this. Built in CI — Gradle 8.11 · JDK 21 · AGP 8.7 · Kotlin 2.0.

```
launch ─▶ base URL set? ──no─▶ 🧩 Setup screen
            │yes
            ▼
        reachable? ──online but no──▶ 🔌 Reconfigure   ──offline──▶ 📥 Downloads (offline)
            │yes
            ▼
   🗂️ Catalog ─▶ ▶️ Detail ─▶ 🎬 Player (streams + caches)
                      └─▶ ⬇️ Download ─▶ plays offline
```

## ✨ Features

| | |
|---|---|
| 📱📺 **One APK, both** | dual `LAUNCHER` + `LEANBACK_LAUNCHER`; leanback/touchscreen `required=false` |
| 🎮 **D-pad + touch** | focusable cards w/ visible focus; player: center=play/pause, ←/→=seek, BACK=exit |
| 🗂️ **Browse / play** | `GET /api/videos` grid → detail → ExoPlayer |
| ⚡ **Cache while playing** | streamed bytes cached (instant re-watch/scrub) |
| 📥 **Offline download** | full download → **plays with no network**; Downloads screen works offline |
| 🔄 **Self-update** | checks `/api/app/latest`, downloads APK, verifies SHA-256, installs |
| 🧩 **Runtime base URL** | set on first run, changeable in Settings (🔒 math-gated) |
| 🎨 **Themes** | light/dark, follows system by default + manual toggle |

## ⚙️ Backend URL

Runtime setting (not hardcoded): entered on first run, editable in **Settings → Backend**,
stored in `SharedPreferences`, survives upgrades. Changing it is **gated by a math quiz**
(`17 × 7`) so kids can't. Build-time `-PbackendBaseUrl=…` only pre-fills the field.

## 💾 Cache & offline

```
SimpleCache (8 GB LRU)  ◀── streaming writes ──  Player
       ▲                                          
       └── DownloadManager (full download) ── Downloads screen ──▶ offline playback
```

🔑 Keyed by **video id** (`customCacheKey`), not the rotating presigned URL — so cache &
downloads survive URL refresh **and** app upgrades. *(See `CLAUDE.md` — don't change.)*

## 🔄 Self-update — install permission ⚠️

`REQUEST_INSTALL_PACKAGES` isn't enough on API 26+: the user must grant *"install unknown
apps"*. The app detects this and deep-links to the setting. For managed TV fleets use a
privileged/device-owner installer.

## 🔑 Signing

| | |
|---|---|
| **Default** | committed convenience keystore `keystore/ci-signing.jks` (alias `ci`, pass `tvvideohub`) — so local & CI builds are consistently signed with **zero setup** |
| ⚠️ **Warning** | that keystore + password are **public** in this repo. Convenient for auto-builds only — **NOT for production**. Anyone could re-sign an APK with it. |
| **Production** | override with **your own** keystore via env/`-P`: `KEYSTORE_FILE`, `KEYSTORE_PASSWORD`, `KEY_ALIAS`, `KEY_PASSWORD` (e.g. `ANDROID_KEYSTORE_BASE64` secret in CI) |

Android only updates an app when the new APK is signed with the **same** key, so keep one
fixed key across releases.

## 📥 Get the app

| source | link |
|---|---|
| GitHub (latest CI build) | `https://github.com/lisenhuang/tv-video-hub/releases/latest/download/tv-video-hub.apk` |
| Your backend (fixed path) | `https://<your-backend>/api/app/download` → always the latest signed APK |

The repo link also appears in the root README so visitors can click-download it.

## 🚀 Build & sideload

```sh
cd android-tv
gradle wrapper --gradle-version 8.11.1          # generates ./gradlew (not committed)
./gradlew :app:assembleRelease -PbackendBaseUrl=https://media.your-host
adb install -r app/build/outputs/apk/release/app-release.apk
```

## 🗺️ Layout

```
app/src/main/java/com/tvvideohub/tv/
├── core/        SettingsStore (url+theme), Connectivity
├── data/        DTOs · Retrofit api · ApiClient (runtime base URL) · CatalogRepository
├── ui/          MainActivity(root gate) · RootViewModel · Catalog · Detail · Downloads
│                · Settings · Theme · components(Input, ParentalGate)
├── player/      PlayerActivity (ExoPlayer via shared cache)
├── download/    DownloadUtil · MediaDownloadService · models
└── update/      UpdateManager (download · verify · install)
```
