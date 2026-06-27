# tv-video-hub

Monorepo for a **private Android TV video playback app** and its **backend media service**.

```
tv-video-hub/
├── backend/          .NET 10 media service (ASP.NET Core minimal API)
│                     · video catalog + playback URLs
│                     · "is there a new version?" endpoint + the APK itself (served from wwwroot/app)
│                     · storage: S3-compatible (R2 / AWS S3 / MinIO / …) OR local disk (videos only)
│                     · database: Cloudflare D1 OR self-hosted SQL (SQLite/Postgres/SQL Server)
│                     · zero-env: configured via the /admin dashboard
├── android-tv/       Android TV app (Kotlin · Compose for TV · Media3/ExoPlayer)
│                     · browses & plays videos from the backend
│                     · self-updates by checking the backend version endpoint
└── .github/workflows/
    └── backend-build.yml    builds/tests the backend
```

The two halves are wired together by the HTTP/JSON contract below. **Both sides must
agree on this contract** — if you change a field, change it in both `backend/` and
`android-tv/`.

---

## 📥 Download the app

The APK is committed in this repo and shipped inside the backend, so there's a single source:

| source | path |
|--------|------|
| 📦 **Committed in the backend** | **[`backend/MediaHub.Api/wwwroot/app/app-release.apk`](backend/MediaHub.Api/wwwroot/app/app-release.apk)** |
| 🌐 **Live download (a running backend)** | `{your-backend}/app/app-release.apk` |

> ⚠️ Signed with the repo's **public convenience keystore** (**not for production** — see
> [`android-tv/README.md`](android-tv/README.md#-signing)). The APK is **served by the backend
> itself** (the committed static file above), not by any third-party host. Inside the app,
> launch checks `GET /api/app/latest`; that response carries the **download URL** for the newest
> APK — composed from the backend's own base URL — and the app downloads it and pops an
> **"Update available"** modal when the reported `versionCode` is newer.
>
> The download URL is **not hard-coded in the app** — the version-check API builds it from the
> backend's base URL + a relative path (`AppRelease.DownloadPath`), so it always points back at
> whichever backend the app is configured to use, with **no app update required**.

---

## How it fits together

```
                 ┌──────────────────────────┐   ┌──────────────────────────┐
                 │  S3-compatible storage   │   │   Pluggable database     │
                 │  (R2 / AWS / MinIO / …)   │   │   D1 or SQL (SQLite/     │
                 │  · video files           │   │   Postgres/SQL Server)   │
                 │                          │   │   · videos               │
                 └───────────▲──────────────┘   └──────────▲───────────────┘
                         │ S3 API / presign       │ D1 HTTP / EF Core
                 ┌───────┴───────────────────────┴──────────────┐
                 │           backend  (.NET 10, container)        │
                 │   GET  /api/videos          list catalog       │
                 │   GET  /api/videos/{id}     details + play URL  │
                 │   GET  /api/app/latest      version + APK URL   │
                 │   GET  /app/app-release.apk the APK (static)    │
                 │   POST /api/videos          admin adds a video  │
                 └───────▲────────────────────────────────────────┘
                         │ HTTPS / JSON
                 ┌───────┴────────────────────────────────────────┐
                 │            Android TV app                        │
                 │   on launch → GET /api/app/latest → self-update  │
                 │   downloads the APK from the SAME backend        │
                 │     (GET /app/app-release.apk)                   │
                 │   browse    → GET /api/videos                    │
                 │   play      → GET /api/videos/{id} → ExoPlayer   │
                 └─────────────────────────────────────────────────┘
```

---

## Where to run it — two setups

The backend is one small container; pick where it lives based on whether you want it
reachable only at home or from anywhere.

> 📱 **You don't bake the backend URL into the app.** On **first launch** the app shows a
> setup screen — type your backend's address, tap **Test**, and **Save**. It's stored
> on-device and you can change it later in **Settings**. (The same APK works against any
> backend; nothing is hard-coded per install.)

### 🏠 1. At home — on a PC or your OpenWRT router *(recommended for most people)*

Run the backend on a machine on your LAN — a spare PC/mini-PC, a NAS, or an OpenWRT
router that can run containers. **Use Docker** — it's the simplest, self-contained way
to run it (see [`backend/README.md`](backend/README.md#run-in-docker)):

```bash
cd backend
make up        # → http://localhost:8080/admin
```

Then, on the app's first-run setup screen, enter the **host's LAN IP and port**
(not `localhost` — that would mean the TV/phone itself). Find the host's IP
(e.g. `192.168.1.50`) and type:

```
http://192.168.1.50:8080
```

- ⚠️ **Include the `http://`** — if you leave the scheme off, the app assumes `https://`,
  which a plain-LAN backend won't answer. For home use you want explicit `http://`.
- ✅ No domain, no public IP, no port-forwarding — everything stays on the LAN.
- ✅ The TV and the backend just need to be on the **same network**.
- 💡 Give the host a **static / DHCP-reserved IP** so the saved URL keeps working.

### 🌐 2. On a server — reachable from anywhere *(needs a domain)*

Run the backend on a VPS / cloud host so the app works off your home network. Here you
normally want a **domain name** pointing at the server (DNS `A`/`AAAA` record), plus
HTTPS (a reverse proxy such as Caddy/Nginx/Traefik terminating TLS) so playback and the
`/admin` dashboard run over `https://`. On the app's setup screen enter that domain:

```
https://media.example.com
```

- 🔐 Prefer **HTTPS** — the app *permits* cleartext (so `http://` works anywhere), but a
  server on the public internet should use TLS so playback URLs and traffic aren't sent in
  the clear. Cleartext is really meant for the LAN setup above.
- 🌍 Works from any network (mobile data, a friend's house), not just home.
- 🧾 You'll manage a domain, DNS, and certificates (most reverse proxies auto-issue
  Let's Encrypt certs).

> Either way the backend itself is identical — only **where it runs** and **what URL you
> enter in the app** differ. Storage (R2/S3 or local disk) and the database (D1 or SQL)
> are configured the same way at `/admin` in both setups.

---

## API contract (v1)

Base URL is configured per environment. All responses are JSON (`application/json`).
Read endpoints are public (or gated by an optional device key); write endpoints
(`POST`) require the `X-Api-Key` header.

### `GET /api/health`
Liveness probe + identity signature (lets a client confirm a URL is genuinely this
backend, not just any server returning 200).
→ `200 { "status": "ok", "service": "tv-video-hub", "api": "v1" }`

The `service`/`api` fields are additive; clients that only check the HTTP status still work.

### `GET /api/videos`
List the playable catalog, newest first.

```jsonc
// 200
{
  "videos": [
    {
      "id": "0f8c…",              // string id
      "title": "Big Buck Bunny",
      "description": "…",
      "thumbnailUrl": "https://…", // may be null
      "durationSeconds": 596,       // may be null if unknown
      "createdAt": "2026-06-26T10:00:00Z"
    }
  ]
}
```

### `GET /api/videos/{id}`
Details for one video **plus a ready-to-play URL**. The `playbackUrl` is a
short-lived URL (default TTL 6h) that ExoPlayer can stream directly — a presigned R2/S3
URL, or, when the **Local disk** provider is selected, an HMAC-signed
`{backend}/api/media/…` URL served by the backend itself (range-capable). Either way the
app just streams the URL — no app change.

```jsonc
// 200
{
  "id": "0f8c…",
  "title": "Big Buck Bunny",
  "description": "…",
  "thumbnailUrl": "https://…",
  "durationSeconds": 596,
  "playbackUrl": "https://<acct>.r2.cloudflarestorage.com/…?X-Amz-Signature=…",
  "playbackUrlExpiresAt": "2026-06-26T16:00:00Z",
  "mimeType": "video/mp4",
  "createdAt": "2026-06-26T10:00:00Z"
}
// 404 if not found
```

### App self-update flow

The APK is **committed in the backend** (`wwwroot/app/app-release.apk`) and served directly
as a static file. On launch the app asks the backend "what's the newest version, and where do
I get it?"; the backend answers with a `versionCode` and a **`downloadUrl`** (its own base URL
+ the APK's relative path), and the app downloads the APK from that **same backend**. Because
the URL is composed by the API (not hard-coded in the app), it always points at whichever
backend the app is configured to use — **no app update needed**.

```text
App self-update (runs on every launch)

  ┌───────────────┐                ┌──────────────────────────────────────┐
  │ Android TV app│                │             backend (.NET)            │
  └───────┬───────┘                └───────────────────┬──────────────────┘
          │                                            │
          │ 1. GET /api/app/latest                     │
          │ ────────────────────────────────────────────▶│ reads AppRelease config; builds
          │   200 { versionCode, sha256, downloadUrl }   │ downloadUrl = this backend's base
          │ ◀────────────────────────────────────────────│ + /app/app-release.apk
          │   (204 if nothing is published)            │
          │ 2. versionCode > installed ?               │
          │      • no  → up to date, stop               │
          │      • yes → "Update available"             │
          │                                            │
          │ 3. GET downloadUrl (the committed APK)      │
          │ ────────────────────────────────────────────▶│ serves wwwroot/app/app-release.apk
          │ ◀──────────────────── APK bytes ──────────────│ (static file)
          │ 4. verify sha256, then launch              │
          │    the Android package installer            │
          ▼                                            │
```

### `GET /api/app/latest`
The newest APK's metadata. The app calls this on launch, compares `versionCode` to its own
`BuildConfig.VERSION_CODE`, and if newer downloads `downloadUrl` and verifies `sha256`.
The APK is served by this backend at `/app/app-release.apk`; `downloadUrl` is composed from
the request's own base URL, so it points back here.

```jsonc
// 200
{
  "versionCode": 1,                  // monotonic integer; app updates if this > installed
  "versionName": "1.0.0",
  "notes": "",                       // changelog, may be empty
  "downloadUrl": "https://your-backend.example.com/app/app-release.apk",
  "sizeBytes": 0,                    // optional, informational
  "sha256": "…",                     // hex digest of the apk, for integrity check
  "minSdk": 23,
  "publishedAt": "2026-06-26T09:00:00Z"
}
// 204 if no release is configured (AppRelease.VersionCode <= 0, or no download path/url)
```

All of these fields come from the backend's **`AppRelease` config section** (appsettings /
env — see [`backend/MediaHub.Api/appsettings.json`](backend/MediaHub.Api/appsettings.json)).
`downloadUrl` is built from `AppRelease.DownloadPath` (default `/app/app-release.apk`) prefixed
with the request's base URL; set the optional `AppRelease.DownloadUrl` only to host the APK
elsewhere. Whenever you rebuild the APK and commit it over `wwwroot/app/app-release.apk`, bump
`versionCode` **and** set `sha256`/`sizeBytes` to that file's values (the app verifies the
download's hash; a stale `sha256` fails the install). There is no object storage, DB release
row, or upload endpoint.

### `POST /api/videos`   *(auth: `X-Api-Key`)*
Register a video. Either upload bytes (`multipart/form-data` with a `file` part) or
reference an object already in R2 (`application/json` with `objectKey`).

```jsonc
// application/json body
{ "title": "…", "description": "…", "objectKey": "videos/foo.mp4",
  "thumbnailUrl": "https://…", "durationSeconds": 596, "mimeType": "video/mp4" }
// 201 → the created video (same shape as GET /api/videos/{id} minus playbackUrl)
```

---

## Configuration (backend)

**Zero env required.** Boot the backend and configure everything at **`/admin`**.

- 💾 **Only the database connection is on disk** (`App_Data/db.json`). The **admin
  account, object-storage config, and release API key all live IN THE DATABASE** (tables
  `admins`, `app_config`). Setup order: **connect a database → create the admin → set
  storage + release key.**
- 🗄️ **Database (pluggable):** Cloudflare **D1** *or* self-hosted SQL via EF Core —
  **SQLite / PostgreSQL / SQL Server** (MySQL selectable; provider not bundled, see
  `backend/README.md`).
- 📦 **Object storage:** any **S3-compatible** store (R2 default; AWS S3, MinIO, B2, …),
  **or Local disk** — store videos/APKs on the server's own filesystem (a media directory,
  default `App_Data/media`), served at `/api/media/…` via short-lived HMAC-signed,
  range-capable URLs. Persist that directory (Docker volume). See `backend/README.md`.

Only the DB connection can be seeded via env (`Database__Provider`,
`Database__ConnectionString`, `Cloudflare__*` for D1). 💾 **Stateless upgrades:** persist
the `App_Data` volume (or seed the DB connection via env) and a new container reuses the
same DB — and therefore the admin, storage, and all data, which live in the DB. Full
details in [`backend/README.md`](backend/README.md).

## Configuration (android)

**The backend URL is not baked into the app.** On **first launch** the app shows a setup
screen — the user types the backend's address, taps **Test** (a live reachability check),
and **Saves** it. The value is stored on-device (SharedPreferences) and can be changed
later in **Settings**, so the same APK works against any backend.

The build-time `-PbackendBaseUrl=…` Gradle property (default `https://media.example.com`)
only **pre-fills** that first-run field — it's a convenience default, not a hard-coded URL.
See [`android-tv/README.md`](android-tv/README.md).

## Status

This is a working scaffold with real implementations on both sides. The backend is built
and tested in CI; the Android APK is committed in the backend (`wwwroot/app/app-release.apk`)
and served directly, and the version-check endpoint composes the app's download URL from the
backend's own base URL. No .NET / Android SDK is required locally to read or review the code.
See each subproject's README for how to run it.
