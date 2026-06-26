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
        versionCode = 1
        versionName = "1.0.0"

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
    val keystorePath: String = System.getenv("KEYSTORE_FILE")
        ?: (project.findProperty("KEYSTORE_FILE") as String?)
        ?: rootProject.file("keystore/ci-signing.jks").absolutePath
    val storePass = System.getenv("KEYSTORE_PASSWORD")
        ?: (project.findProperty("KEYSTORE_PASSWORD") as String?) ?: "tvvideohub"
    val keyAliasName = System.getenv("KEY_ALIAS")
        ?: (project.findProperty("KEY_ALIAS") as String?) ?: "ci"
    val keyPass = System.getenv("KEY_PASSWORD")
        ?: (project.findProperty("KEY_PASSWORD") as String?) ?: "tvvideohub"

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
