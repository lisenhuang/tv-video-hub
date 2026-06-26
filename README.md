# tv-video-hub

Monorepo for a **private Android TV video playback app** and its **backend media service**.

```
tv-video-hub/
├── backend/          .NET 10 media service (ASP.NET Core minimal API)
│                     · video catalog + playback URLs
│                     · APK hosting + "is there a new version?" endpoint
│                     · storage: S3-compatible (R2 / AWS S3 / MinIO / …) OR local disk
│                     · database: Cloudflare D1 OR self-hosted SQL (SQLite/Postgres/SQL Server)
│                     · zero-env: configured via the /admin dashboard
├── android-tv/       Android TV app (Kotlin · Compose for TV · Media3/ExoPlayer)
│                     · browses & plays videos from the backend
│                     · self-updates by checking the backend version endpoint
└── .github/workflows/
    ├── android-build.yml   builds the APK and publishes it to the backend
    └── backend-build.yml    builds/tests the backend
```

The two halves are wired together by the HTTP/JSON contract below. **Both sides must
agree on this contract** — if you change a field, change it in both `backend/` and
`android-tv/`.

---

## 📥 Download the app

| source | link |
|--------|------|
| 📦 **GitHub release (latest CI build)** | **[`tv-video-hub.apk`](https://github.com/lisenhuang/tv-video-hub/releases/latest/download/tv-video-hub.apk)** |
| 🌐 Your backend (fixed path) | `https://<your-backend>/api/app/latest.apk` (or `/api/app/download`) → always the latest signed APK |

> ⚠️ Signed with the repo's **public convenience keystore** (auto-build only, **not for
> production** — see [`android-tv/README.md`](android-tv/README.md#-signing)). The GitHub link
> appears once CI has published a release on `main`. Inside the app, launch checks
> `GET /api/app/latest` and pops an **"Update available"** modal when a newer build exists.

---

## How it fits together

```
                 ┌──────────────────────────┐   ┌──────────────────────────┐
                 │  S3-compatible storage   │   │   Pluggable database     │
                 │  (R2 / AWS / MinIO / …)   │   │   D1 or SQL (SQLite/     │
                 │  · video files           │   │   Postgres/SQL Server)   │
                 │  · apk files             │   │   · videos · app_releases│
                 └───────────▲──────────────┘   └──────────▲───────────────┘
                         │ S3 API / presign       │ D1 HTTP / EF Core
                 ┌───────┴───────────────────────┴──────────────┐
                 │           backend  (.NET 10, container)        │
                 │   GET  /api/videos          list catalog       │
                 │   GET  /api/videos/{id}     details + play URL  │
                 │   GET  /api/app/latest      newest APK info     │
                 │   GET  /api/app/download    redirect to APK     │
                 │   POST /api/app/releases    CI uploads new APK  │  ◀── GitHub Actions
                 │   POST /api/videos          admin adds a video  │
                 └───────▲────────────────────────────────────────┘
                         │ HTTPS / JSON
                 ┌───────┴────────────────────────────────────────┐
                 │            Android TV app                        │
                 │   on launch → GET /api/app/latest → self-update  │
                 │   browse    → GET /api/videos                    │
                 │   play      → GET /api/videos/{id} → ExoPlayer   │
                 └─────────────────────────────────────────────────┘
```

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

### `GET /api/app/latest`
The newest published APK. The app calls this on launch and compares `versionCode`
to its own `BuildConfig.VERSION_CODE`.

```jsonc
// 200
{
  "versionCode": 12,                 // monotonic integer; app updates if this > installed
  "versionName": "1.3.0",
  "notes": "Bug fixes",              // changelog, may be empty
  "downloadUrl": "https://…/api/app/download?versionCode=12",
  "sizeBytes": 8123456,
  "sha256": "…",                     // hex digest of the apk, for integrity check
  "minSdk": 23,
  "publishedAt": "2026-06-26T09:00:00Z"
}
// 204 if no release has been published yet
```

### `GET /api/app/download?versionCode={code}`
Returns the APK bytes. Implemented as a `302` redirect to a short-lived presigned
object-storage URL (`Content-Type: application/vnd.android.package-archive`). If
`versionCode` is omitted, the latest is served.

### `GET /api/app/latest.apk`
Fixed-path alias for "download the latest APK" — same `302` to the latest signed APK
as `/api/app/download` with no `versionCode`. A stable URL you can share/bookmark.

### `POST /api/app/releases`   *(auth: `X-Api-Key`)*
Called by CI after a successful Android build to publish a new APK. Accepts
`multipart/form-data`:

| field         | type   | notes                                  |
|---------------|--------|----------------------------------------|
| `apk`         | file   | the built `.apk`                       |
| `versionCode` | int    | from `app/build.gradle.kts`            |
| `versionName` | string |                                        |
| `notes`       | string | optional changelog                     |
| `minSdk`      | int    | optional (default 23)                  |

The backend stores the file in R2, records a row in D1, computes the SHA-256, and
returns the stored release (same shape as `/api/app/latest`). Re-posting an existing
`versionCode` replaces it.

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

`android-tv/app/src/main/res/values/config.xml` → `backend_base_url`, or override at
build time with `-PbackendBaseUrl=https://…`. See [`android-tv/README.md`](android-tv/README.md).

## Status

This is a working scaffold with real implementations on both sides. It is built and
tested in CI (no .NET SDK / Android SDK is required locally to read or review it).
See each subproject's README for how to run it.
