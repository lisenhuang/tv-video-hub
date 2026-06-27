# Project Guidelines — tv-video-hub

Monorepo: `backend/` (.NET 10 media service) + `android-tv/` (Android app for phones & TV).
See `README.md` for the architecture and the HTTP/JSON API contract.

## Backward compatibility is a hard constraint (do NOT break old versions)

There are **already-installed apps in the field** and a **live backend**. Every change
must keep working with what's already out there. Treat this as a hard rule, not a
nice-to-have. When a change *could* affect an older app, cached data, or the local
app DB, call it out explicitly in your hand-off and describe the safe upgrade path.

### Android app — never destroy the user's local state on upgrade

An app upgrade **must not wipe or corrupt** anything the user already has on-device:

- **Cached / downloaded videos must survive the upgrade.** Do not change the cache
  directory name, the cache key scheme, or the storage location in a way that orphans
  existing cached bytes. Videos are cached and downloaded keyed by **stable video id**
  (`customCacheKey`), *not* by the (rotating, presigned) playback URL — keep it that
  way so cache entries stay valid across both URL rotation and app updates.
- **The on-device DB (Media3 download index / any app database) must remain readable.**
  You MAY evolve the local schema, but only **additively and with migrations**: new
  columns nullable or defaulted, no destructive drops/renames that make an older DB
  unreadable. After an upgrade the app must open the existing DB and still see
  previously downloaded videos — never reset or recreate it from scratch.
- **No "clear everything on version bump" shortcuts.** Don't delete the cache, the
  download index, or app data just because the version changed.
- If a local-storage change is unavoidable, **migrate in place** (read old → write new)
  and keep the old data working until the migration completes.

### Backend API — never break an older installed app

Older app versions will keep calling the API for a long time. The backend must stay
compatible with every shipped client:

- **Additive, not destructive.** Add new endpoints/fields rather than renaming or
  removing existing ones. Keep existing routes, query params, and JSON field names and
  types stable. An old app must be able to parse today's responses.
- **New response fields must be optional** for old clients (they ignore unknown fields;
  don't *remove* or repurpose fields they depend on).
- **Don't tighten contracts under an old client's feet** — e.g. don't start requiring a
  new header/param on an existing endpoint that old apps don't send. Version the
  endpoint or default it instead.
- The self-update path — `GET /api/app/latest` (its JSON shape, esp. an **absolute**
  `downloadUrl`) plus the static APK at `/app/app-release.apk` — is a contract with the
  *currently installed* app. Changing it can strand devices on an un-updatable version.
  Change with extreme care and keep it backward compatible (see the APK sections below).

### D1 / database migrations (backend)

- Migrations are **additive and forward-only**, applied automatically at startup
  (`CREATE TABLE IF NOT EXISTS`, nullable/defaulted new columns). They must apply
  cleanly on top of the real production database without data loss and without manual
  fix-up. No destructive `DROP`/`DELETE` of existing data unless explicitly approved.

## Always build after changing code (BOTH apps) — non-negotiable

Every time you modify code, **build it and fix every build error before handing off** —
for **both** targets when touched:

- **.NET backend** — run `dotnet build` in `backend/` and resolve all errors (and avoid
  introducing warnings). Never hand off backend code that doesn't compile.
- **Android APK** — build it: `cd android-tv && ./gradlew :app:assembleDebug` (or
  `assembleRelease`). If the Android SDK isn't available locally, say so explicitly and at
  minimum keep it compile-clean (consistent version catalog, imports, opt-ins) so the CI
  build is green; treat a red CI build as a build error to fix, not to ignore.

Do not call a change "done" until the relevant target(s) build. If a build genuinely can't
be run, state why and what you did to keep it buildable.

## Ship the release APK into the backend on every android-tv change — non-negotiable

**The release APK lives IN this repo and is served BY the backend** — there is no GitHub
Release and no CI APK build. A single committed binary at
`backend/MediaHub.Api/wwwroot/app/app-release.apk` ships inside the published image / Docker
build and is served **directly as a static file** at `GET /app/app-release.apk` (the
static-file middleware serves it — no endpoint code reads the bytes). So **whenever you modify
`android-tv/` code, rebuild the release APK and copy it over that committed file** — otherwise
the download serves a stale build.

- **Build the *release* APK** (signed, installable), not debug, and copy it over the committed file:
  ```
  cd android-tv && ./gradlew :app:assembleRelease
  cp app/build/outputs/apk/release/app-release.apk \
     ../backend/MediaHub.Api/wwwroot/app/app-release.apk
  ```
  (If `./gradlew` is missing locally, bootstrap gradle `8.11.1` per `gradle-wrapper.properties`;
  a cached newer gradle that is AGP-compatible also works.)
- **Only ONE APK in the repo** — the newest build, at the fixed path/filename
  `wwwroot/app/app-release.apk`. Don't keep old versions around; overwrite it in place.
  Renaming/moving it breaks the static path (`/app/app-release.apk`) and the composed `downloadUrl`.
- **One universal APK that runs on BOTH armeabi-v7a (ARM v7) and arm64-v8a (ARM v8).** Keep it
  a single universal artifact — **do NOT add ABI splits / per-ABI APKs** (that yields separate
  files and breaks the one fixed download path). A normal release build is already universal (it
  bundles every ABI's libs into the one APK — currently `libandroidx.graphics.path.so` for
  arm64-v8a, armeabi-v7a, x86, x86_64); if you add an NDK/native dependency, keep both ARM ABIs
  packaged — never ship an APK that drops either ARM ABI.
- **Sign with the SAME keystore as previously-shipped builds.** Android installs an update only
  when it is higher-`versionCode` AND identically signed. The default committed key is
  `android-tv/keystore/ci-signing.jks`; if installed devices got a build signed with a different
  key, keep using THAT key or those devices will reject the update ("signatures do not match").
- **Bump the version on every change** (mandatory — see the next section).

## Bump the version AND sync it to the backend on every android-tv change — non-negotiable

Whenever you modify `android-tv/` app code, a version bump **and** a backend version-metadata
sync are part of the change — not optional, not "only for a real OTA". Without both, already-
installed apps never learn an update exists and the self-update flow (`GET /api/app/latest`)
never fires. Do all of this in the same change:

1. **Bump the app version.** In `android-tv/app/build.gradle.kts` increase `versionCode` by 1
   (monotonic integer — never reuse or lower it) and bump `versionName` (e.g. `1.0.4` →
   `1.0.5`). Keep the **same** signing keystore; Android installs an update only when the new
   `versionCode` is strictly higher **and** the signature matches.
2. **Rebuild + commit the release APK** into the backend (per the "Ship the release APK" section
   above), so the bytes served match the new version.
3. **Sync the new version info to the backend** so `GET /api/app/latest` advertises the update.
   That endpoint returns the **`AppRelease` config section** in
   `backend/MediaHub.Api/appsettings.json` (bound via `AppReleaseOptions`, served by
   `AppEndpoints.MapAppEndpoints`). Set it from the **committed APK**, in the SAME change as the
   gradle bump — there is **no CI step to wait on**, and because the served file IS the file you
   just built, its hash always matches:
   - `VersionCode` → the new gradle `versionCode` (the app updates only when this is **greater
     than** its installed build, so this MUST be bumped or no device ever upgrades).
   - `VersionName` → the new gradle `versionName`.
   - `Sha256` → lowercase-hex SHA-256 of the committed APK:
     `shasum -a 256 backend/MediaHub.Api/wwwroot/app/app-release.apk`. The app verifies the
     download against it; a stale/blank hash on a real bump makes the update fail (fail-closed).
   - `SizeBytes` → byte size of that same file (`wc -c < …/app-release.apk`); `Notes` (changelog
     shown in the update prompt) and `PublishedAt` → update to match.
   - `DownloadPath` → leave at `/app/app-release.apk` (the static file). The endpoint composes the
     absolute `downloadUrl` at request time from the caller's base URL + this path, so it always
     points back at the same backend. Only set the optional absolute `DownloadUrl` to host the APK
     somewhere else entirely.
4. **Keep the three in lockstep.** The gradle `versionCode`/`versionName`, the committed
   `wwwroot/app/app-release.apk` bytes, and the backend `AppRelease.VersionCode`/`Sha256` must
   all describe the *same* build. Mismatches break the OTA: an un-bumped backend `VersionCode`
   means no device sees the update; a `Sha256` that doesn't match the served APK makes the
   download get rejected.
5. **Backward compatible — keep `downloadUrl` ABSOLUTE.** The shipped app feeds `downloadUrl`
   straight to Android's `DownloadManager`, which requires an absolute `http(s)` URL — so the
   backend composes an absolute URL from the request base; it must **never** become a bare
   relative path, or every already-installed app is stranded (can't parse it → can't update).
   The response shape is otherwise unchanged, so older apps keep parsing it. Never lower
   `VersionCode`, and never rename/repurpose existing `AppRelease` fields.

## After coding — build, then hand off with "what to do next"

1. **Build it.** Backend: `dotnet build` (fix every error). Android: it builds in CI
   (no SDK locally) — keep the code compile-clean and the version catalog consistent.
2. End with a short **Next steps** section: what to run/deploy, any new config/secret,
   and how to verify.
3. **TL;DR** stating plainly: **Safe for old apps & old data?** (does it stay backward
   compatible per the rules above) and **Migrate first?** (any DB/storage migration
   needed before deploy).
