# tv-video-hub

Monorepo for a **private Android TV video playback app** and its **backend media service**.

```
tv-video-hub/
├── backend/          .NET 10 media service (ASP.NET Core minimal API)
│                     · video catalog + playback URLs
│                     · APK hosting + "is there a new version?" endpoint
│                     · storage: S3-compatible (R2 / AWS S3 / MinIO / …)
│                     · database: Cloudflare D1
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
| 🌐 Your backend (fixed path) | `https://<your-backend>/api/app/download` → always the latest signed APK |

> ⚠️ Signed with the repo's **public convenience keystore** (auto-build only, **not for
> production** — see [`android-tv/README.md`](android-tv/README.md#-signing)). The GitHub link
> appears once CI has published a release on `main`. Inside the app, launch checks
> `GET /api/app/latest` and pops an **"Update available"** modal when a newer build exists.

---

## How it fits together

```
                 ┌──────────────────────────┐   ┌──────────────────────┐
                 │  S3-compatible storage   │   │   Cloudflare D1       │
                 │  (R2 / AWS / MinIO / …)   │   │   (SQLite DB)         │
                 │  · video files           │   │   · videos table      │
                 │  · apk files             │   │   · app_releases      │
                 └───────────▲──────────────┘   └──────────▲───────────┘
                         │ S3 API / presign       │ D1 REST query
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
Liveness probe. → `200 { "status": "ok" }`

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
short-lived presigned R2 URL (default TTL 6h) that ExoPlayer can stream directly.

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
R2 URL (`Content-Type: application/vnd.android.package-archive`). If `versionCode`
is omitted, the latest is served.

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

Credentials via env vars / user-secrets — never commit. **Database = Cloudflare D1**;
**object storage = any S3-compatible store** (R2 default; AWS S3, MinIO, B2, …). Full
list + provider presets in [`backend/README.md`](backend/README.md). Quick reference:

| env var                      | purpose                                         |
|------------------------------|-------------------------------------------------|
| `Cloudflare__AccountId`      | Cloudflare account id (D1)                       |
| `Cloudflare__D1__DatabaseId` | D1 database id                                   |
| `Cloudflare__D1__ApiToken`   | API token with D1 edit permission                |
| `Storage__ServiceUrl`        | S3 endpoint (empty = AWS regional endpoint)      |
| `Storage__Region`            | `auto` (R2) · `us-east-1` (AWS) · region (MinIO) |
| `Storage__AccessKeyId`       | S3 access key id                                 |
| `Storage__SecretAccessKey`   | S3 secret                                        |
| `Storage__VideoBucket`       | bucket holding video files                       |
| `Storage__ApkBucket`         | bucket holding apk files                         |
| `Storage__ForcePathStyle`    | `true` (R2/MinIO) · `false` (AWS virtual-hosted) |
| `Api__Key`                   | shared secret for `X-Api-Key` writes             |

An admin dashboard at **`/admin`** can edit D1 + storage config live (no restart).

## Configuration (android)

`android-tv/app/src/main/res/values/config.xml` → `backend_base_url`, or override at
build time with `-PbackendBaseUrl=https://…`. See [`android-tv/README.md`](android-tv/README.md).

## Status

This is a working scaffold with real implementations on both sides. It is built and
tested in CI (no .NET SDK / Android SDK is required locally to read or review it).
See each subproject's README for how to run it.
