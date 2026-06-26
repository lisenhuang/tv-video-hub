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
- The self-update endpoints (`/api/app/latest`, `/api/app/download`) and their response
  shape are a contract with the *currently installed* app — changing them can strand
  devices on an un-updatable version. Change with extreme care and keep them backward
  compatible.

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

The backend serves a **direct APK download** at `GET /api/app/bundled.apk`, backed by a
**committed** binary at `backend/MediaHub.Api/wwwroot/app/app-release.apk` (it ships inside
the published image / Docker build). So **whenever you modify `android-tv/` code, rebuild the
release APK and copy it over that committed file** — otherwise the download link serves a
stale build.

- **Build the *release* APK** (signed, installable), not debug:
  ```
  cd android-tv && ./gradlew :app:assembleRelease
  cp app/build/outputs/apk/release/app-release.apk \
     ../backend/MediaHub.Api/wwwroot/app/app-release.apk
  ```
  (If `./gradlew` is missing locally, bootstrap gradle `8.11.1` per `gradle-wrapper.properties`.)
- **One universal APK that runs on BOTH armeabi-v7a (ARM v7) and arm64-v8a (ARM v8).** Keep it
  a single universal artifact — **do NOT add ABI splits / per-ABI APKs** (that yields separate
  files and breaks the one fixed download path). The app currently has no native `.so`
  libraries, so a normal release build is already universal; if you ever add an NDK/native
  dependency, package both ARM ABIs into the one APK (e.g. `ndk { abiFilters += listOf(
  "armeabi-v7a", "arm64-v8a") }`) — never ship an APK that drops either ARM ABI.
- **Keep the path & filename stable** (`wwwroot/app/app-release.apk`). The endpoint and the
  committed location are a contract; renaming/moving the file 404s the download link.
- **For a real over-the-air update**, also bump `versionCode` (and `versionName`) in
  `android-tv/app/build.gradle.kts` and sign with the **same** keystore — Android only installs
  an update that is higher-versioned and identically signed. Don't change the signing key.
- This is **additive and backward compatible** with the existing self-update flow
  (`/api/app/latest`, `/api/app/download`, `/api/app/releases`) — leave those endpoints intact.

## After coding — build, then hand off with "what to do next"

1. **Build it.** Backend: `dotnet build` (fix every error). Android: it builds in CI
   (no SDK locally) — keep the code compile-clean and the version catalog consistent.
2. End with a short **Next steps** section: what to run/deploy, any new config/secret,
   and how to verify.
3. **TL;DR** stating plainly: **Safe for old apps & old data?** (does it stay backward
   compatible per the rules above) and **Migrate first?** (any DB/storage migration
   needed before deploy).
