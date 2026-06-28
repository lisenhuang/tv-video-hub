plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.serialization)
    alias(libs.plugins.compose.compiler)
}

// Backend base URL: override at build time with -PbackendBaseUrl=https://your-host
// Falls back to a placeholder so the project still builds out of the box.
val backendBaseUrl: String =
    (project.findProperty("backendBaseUrl") as String?)?.trimEnd('/')
        ?: "https://media.example.com"

android {
    namespace = "com.tvvideohub.tv"
    compileSdk = 35

    defaultConfig {
        applicationId = "com.tvvideohub.tv"
        minSdk = 23
        targetSdk = 34
        // 15 / 1.0.14 — reach downloads from the launch gate: the access-code screen and the
        // first-run setup screen now show a "Downloads" button so a user who's already downloaded
        // videos can watch them offline even without a valid access code or a configured backend
        // URL (matches what the offline / reconfigure screens already offered). UI-only; no API,
        // cache-key, or DB change, so old apps and existing downloads are unaffected.
        // 13 / 1.0.12 — loop catalog playback: the player loops the catalog (repeats the single
        // video when only one is present).
        // 12 / 1.0.11 — keep the screen awake during a self-update download: while the new APK is
        // downloading, the catalog now holds keepScreenOn (same approach the player uses during
        // playback) so the Android TV screensaver/daydream can't kick in mid-download and interrupt
        // it; released as soon as the download ends so the screensaver still appears when idle.
        // 11 / 1.0.10 — full-episode caching, visible + reliable: the player now buffers up to ~5 min
        // ahead (byte-capped at 128 MB so it can't OOM) so the seek bar fills far ahead and keeps
        // filling while paused; a "Caching NN%" indicator shows the background prefetch pulling the
        // WHOLE episode to disk (advances even while paused); and the prefetch now re-fetches a fresh
        // presigned URL + resumes on expiry, so long episodes reliably reach 100%.
        // (10 / 1.0.9 — access-code gate + update polish: optional admin-set access code required to
        // browse content (sent as X-Access-Code; entry screen on launch); the update modal now traps
        // D-pad focus to its own buttons; and a forceUpdate flag from /api/app/latest hides "Later"
        // and blocks BACK for mandatory updates.
        // 9 / 1.0.8 — smoother playback + list previews: the background prefetch now keeps caching
        // ahead during normal playback (it only yields to the player while actually rebuffering), so
        // a slow/bursty network is far less likely to stall the picture; and the catalog list now
        // generates frame previews on the spot (bounded) for videos with no server thumbnail.
        // 8 / 1.0.7 — version-only bump (no code changes) to test the in-app self-update flow:
        // an installed v7 should see this as an available update and exercise the progress bar +
        // auto-restart.
        // 7 / 1.0.6 — self-update UX: the in-app updater now shows a download progress bar +
        // percentage, the app auto-restarts after the new APK installs (MY_PACKAGE_REPLACED), and
        // Settings shows the running app version.
        // 6 / 1.0.5 — cache-while-playing: a full-speed background prefetch pulls the whole video
        // into the shared cache as it plays (no Download click needed), so playback no longer
        // stalls on a slow/bursty link. It yields to live playback via a PriorityTaskManager and
        // shares the same cache + video-id key, so bytes are fetched once, never duplicated.
        // 5 / 1.0.4 kept the screen awake during playback (FLAG_KEEP_SCREEN_ON held while the
        // player is actually playing) so the Android TV screensaver/daydream no longer kicks in
        // mid-video; released on pause/stop so it can still appear when genuinely idle.
        // 4 / 1.0.3 added client-side video previews (extract a frame when the API has no
        // thumbnail, cached by stable id) + resume playback position per episode on reopen.
        // 3 / 1.0.2 added touch support: tv-material clickables are D-pad-only, so phones couldn't
        // press any button/card; AppButton/tapClickable add a touch-tap path, D-pad still works.
        // 2 / 1.0.1 fixed the first-launch crash: CatalogRepository no longer reads
        // ApiClient.service eagerly.) Same signing key, higher versionCode → installable update.
        versionCode = 15
        versionName = "1.0.14"

        // Exposed to Kotlin via BuildConfig.BACKEND_BASE_URL.
        buildConfigField("String", "BACKEND_BASE_URL", "\"$backendBaseUrl\"")
        // Also exposed as a string resource (@string/backend_base_url) so it can be
        // referenced from config.xml / overridden per build flavor if desired.
        resValue("string", "backend_base_url", backendBaseUrl)
    }

    // Release signing. Android only updates an installed app when the new APK is signed
    // with the SAME key, so every build must use one persistent keystore.
    //
    // Default = the committed CONVENIENCE keystore (keystore/ci-signing.jks), so local and
    // CI builds are consistently signed and installable with zero setup.
    // ⚠️ That keystore + password are PUBLIC (in the repo) — convenient for auto-build only,
    //    NOT for production. For prod, override with your OWN keystore via env / -P:
    //    KEYSTORE_FILE, KEYSTORE_PASSWORD, KEY_ALIAS, KEY_PASSWORD. See android-tv/README.md.
    // Resolve from env, then -P property, falling back to the default. IMPORTANT: CI maps
    // undefined secrets to EMPTY-STRING env vars (not unset), so blanks must be treated as
    // absent — otherwise release builds try to sign with an empty password and fail.
    fun signingValue(name: String, default: String): String =
        System.getenv(name)?.takeIf { it.isNotBlank() }
            ?: (project.findProperty(name) as String?)?.takeIf { it.isNotBlank() }
            ?: default

    val keystorePath: String =
        System.getenv("KEYSTORE_FILE")?.takeIf { it.isNotBlank() }
            ?: (project.findProperty("KEYSTORE_FILE") as String?)?.takeIf { it.isNotBlank() }
            ?: rootProject.file("keystore/ci-signing.jks").absolutePath
    val storePass = signingValue("KEYSTORE_PASSWORD", "tvvideohub")
    val keyAliasName = signingValue("KEY_ALIAS", "ci")
    val keyPass = signingValue("KEY_PASSWORD", "tvvideohub")

    signingConfigs {
        create("release") {
            storeFile = file(keystorePath)
            storePassword = storePass
            keyAlias = keyAliasName
            keyPassword = keyPass
        }
    }

    buildTypes {
        debug {
            isMinifyEnabled = false
        }
        release {
            isMinifyEnabled = false
            proguardFiles(
                getDefaultProguardFile("proguard-android-optimize.txt"),
                "proguard-rules.pro"
            )
            signingConfig = signingConfigs.getByName("release")
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }

    kotlinOptions {
        jvmTarget = "11"
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }

    // Don't let lint's "vital" release checks fail the build in CI. The launcher icon
    // alias (mipmap → drawable vector) resolves fine at runtime; lint only flags it as a
    // type mismatch. Keep lint as advisory rather than a hard release gate.
    lint {
        checkReleaseBuilds = false
        abortOnError = false
    }

    packaging {
        resources {
            excludes += "/META-INF/{AL2.0,LGPL2.1}"
        }
    }
}

dependencies {
    // Core / lifecycle
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(libs.androidx.activity.compose)

    // Compose (BOM-managed versions)
    val composeBom = platform(libs.androidx.compose.bom)
    implementation(composeBom)
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.graphics)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.foundation)
    debugImplementation(libs.androidx.compose.ui.tooling)

    // Compose for TV (tv-material for focusable Cards/Surfaces; the catalog grid uses
    // the standard Compose LazyVerticalGrid, which now handles TV focus — tv-foundation
    // is deprecated and only ever shipped alpha builds, so we don't depend on it).
    implementation(libs.androidx.tv.material)

    // Media3 / ExoPlayer
    implementation(libs.androidx.media3.exoplayer)
    implementation(libs.androidx.media3.ui)
    implementation(libs.androidx.media3.common)
    // Caching while streaming + offline downloads.
    implementation(libs.androidx.media3.datasource)
    implementation(libs.androidx.media3.database)

    // Networking
    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization.converter)
    implementation(libs.okhttp)
    implementation(libs.okhttp.logging.interceptor)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.kotlinx.coroutines.android)

    // Image loading
    implementation(libs.coil.compose)
}

// Prints the app version so CI can tag the published release.
//   gradle -q :app:printVersion  ->  versionCode=1\nversionName=1.0.0
tasks.register("printVersion") {
    doLast {
        println("versionCode=${android.defaultConfig.versionCode}")
        println("versionName=${android.defaultConfig.versionName}")
    }
}
