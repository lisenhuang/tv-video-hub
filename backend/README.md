# MediaHub backend (.NET 10)

ASP.NET Core minimal API that serves the video catalog to the Android TV app,
generates short-lived streaming URLs, and hosts APK builds for self-update.

- 🗄️ **Database:** Cloudflare **D1** (SQLite) via the D1 HTTP query API — no EF Core,
  no local file. Schema auto-created at startup (`CREATE TABLE IF NOT EXISTS`, additive).
- 📦 **Object storage:** any **S3-compatible** store (Cloudflare R2, AWS S3, MinIO,
  Backblaze B2, …) via `AWSSDK.S3`. Videos/APKs stream/download via presigned URLs.
  Defaults are R2-ready out of the box.

See the [repo root README](../README.md) for the full HTTP/JSON contract.

## Project layout

```
MediaHub.Api/
├── Program.cs               composition root, DI, JSON, auth, startup schema init
├── Options/                 CloudflareOptions (D1), StorageOptions (S3), ApiOptions, SettingsOptions
├── Data/
│   ├── D1Client.cs          HTTP client for the D1 query API (parameterized SQL)
│   ├── DatabaseInitializer.cs   ensures tables exist at boot (videos, app_releases, admins)
│   ├── VideoRepository.cs
│   ├── VideoCreationService.cs   shared create/delete logic (public + admin)
│   ├── AppReleaseRepository.cs
│   └── AdminRepository.cs
├── Settings/                runtime-editable config (live-reloaded)
│   ├── PersistedSettings.cs            persisted override shape (Cloudflare + Storage)
│   ├── SettingsStore.cs               read/write the JSON file
│   ├── EffectiveCloudflareConfig.cs   resolved D1 snapshot
│   ├── EffectiveStorageConfig.cs      resolved S3 storage snapshot
│   └── SettingsProvider.cs            singleton; live source for D1Client/S3Storage
├── Storage/S3Storage.cs     presigned GET URLs + upload/delete/exists against any S3 store
├── Auth/
│   ├── ApiKeyFilter.cs      X-Api-Key guard for write endpoints
│   └── PasswordHasher.cs    PBKDF2/SHA-256 (framework crypto, no packages)
├── Endpoints/
│   ├── VideoEndpoints.cs    GET /api/videos, GET /api/videos/{id}, POST /api/videos
│   ├── AppEndpoints.cs      GET /api/app/latest|download, POST /api/app/releases
│   └── AdminEndpoints.cs    cookie-authed /api/admin/* dashboard API
├── wwwroot/admin/           the static admin dashboard (index.html + app.js + styles.css)
└── Models/                  entities + DTOs
```

## Endpoints

| Method | Route                          | Auth        | Purpose                              |
|--------|--------------------------------|-------------|--------------------------------------|
| GET    | `/api/health`                  | —           | liveness                             |
| GET    | `/api/videos`                  | —           | list catalog                         |
| GET    | `/api/videos/{id}`             | —           | details + presigned `playbackUrl`    |
| POST   | `/api/videos`                  | `X-Api-Key` | register/upload a video              |
| GET    | `/api/app/latest`              | —           | newest APK metadata (204 if none)    |
| GET    | `/api/app/download?versionCode`| —           | 302 → presigned APK URL              |
| POST   | `/api/app/releases`            | `X-Api-Key` | publish a build (used by CI)         |

### Admin dashboard (`/admin`)

Vanilla HTML/CSS/JS under `wwwroot/admin/` (light/dark theme) to manage the catalog
and D1/storage config from a browser. **Cookie auth** — an *additional* path that
doesn't affect public endpoints or the `X-Api-Key` write path. Single-admin: first
run creates one admin via a setup form; afterwards setup is permanently closed.

| Method | Route                          | Auth     | Purpose                                   |
|--------|--------------------------------|----------|-------------------------------------------|
| GET    | `/admin`                       | —        | serves the dashboard SPA                  |
| GET    | `/api/admin/setup-state`       | —        | `{ needsSetup, authenticated }`           |
| POST   | `/api/admin/setup`             | —¹       | create the first admin + sign in (409 if one exists) |
| POST   | `/api/admin/login`             | —        | sign in (cookie); 401 on bad creds        |
| POST   | `/api/admin/logout`            | cookie   | sign out                                  |
| GET    | `/api/admin/me`                | cookie   | `{ username }`                            |
| POST   | `/api/admin/change-password`   | cookie   | change the existing admin's password      |
| GET    | `/api/admin/videos`            | cookie   | list catalog                              |
| POST   | `/api/admin/videos`            | cookie   | add a video (multipart upload OR JSON ref)|
| DELETE | `/api/admin/videos/{id}`       | cookie   | delete the D1 row **and** the storage object |
| GET    | `/api/admin/releases`          | cookie   | list app releases (read-only)             |
| GET    | `/api/admin/settings`          | cookie   | effective D1 + storage config, **secrets masked** |
| PUT    | `/api/admin/settings`          | cookie   | edit config (blank secret = keep current) |
| POST   | `/api/admin/settings/test`     | cookie   | probe D1 (`SELECT 1`) + storage (list)    |

¹ `setup` is public but only succeeds while there are **zero** admins.

The dashboard never receives full secrets — `GET/PUT /api/admin/settings` return only
`{ isSet, last4 }` per secret. Saving a blank secret field leaves the stored value
unchanged.

## ⚙️ Configuration

Bind via env vars (double underscore = nesting) or `dotnet user-secrets`. **Never commit real values.**

**🗄️ Database — Cloudflare D1**

| variable                     | req | notes                              |
|------------------------------|-----|------------------------------------|
| `Cloudflare__AccountId`      | yes | account id                         |
| `Cloudflare__D1__DatabaseId` | yes | D1 database id                     |
| `Cloudflare__D1__ApiToken`   | yes | API token, **D1 Edit** permission  |

**📦 Object storage — S3-compatible (`Storage__*`)**

| variable                          | req | notes                                            |
|-----------------------------------|-----|--------------------------------------------------|
| `Storage__ServiceUrl`             | no  | S3 endpoint; **empty = AWS regional endpoint**   |
| `Storage__Region`                 | no  | `auto` (R2) · `us-east-1` (AWS) · region (MinIO) |
| `Storage__AccessKeyId`            | yes | access key id                                    |
| `Storage__SecretAccessKey`        | yes | secret                                           |
| `Storage__VideoBucket`            | no  | default `videos`                                 |
| `Storage__ApkBucket`              | no  | default `apks`                                   |
| `Storage__ForcePathStyle`         | no  | default `true` (R2/MinIO); AWS virtual-hosted → `false` |
| `Storage__PresignTtlMinutes`      | no  | default `360`                                    |
| `Storage__DisablePayloadSigning`  | no  | default `true` (R2 needs it)                     |
| `Storage__UseChecksumWhenRequired`| no  | default `true` (clean R2 presigns)               |

**Other**

| variable             | req  | notes                                          |
|----------------------|------|------------------------------------------------|
| `Api__Key`           | yes* | enables `X-Api-Key` writes (`503` if empty)    |
| `Settings__FilePath` | no   | runtime settings file; default `App_Data/settings.json` |

\* Empty `Api__Key` ⇒ write endpoints return `503` (fail-closed).

**Provider presets:**

| Provider | `ServiceUrl`                              | `Region`    | `ForcePathStyle` |
|----------|-------------------------------------------|-------------|------------------|
| R2       | `https://<acct>.r2.cloudflarestorage.com` | `auto`      | `true`           |
| AWS S3   | *(empty)*                                 | `us-east-1` | `false`          |
| MinIO    | `http://minio:9000`                       | `us-east-1` | `true`           |

### 🔄 Runtime-editable config (dashboard)

Env vars are the **bootstrap defaults**. A logged-in admin can view/edit D1 +
storage config in the dashboard; it persists to `Settings__FilePath` (default
`App_Data/settings.json`, dir auto-created). Effective config = **persisted file
merged over env/appsettings** (a persisted field wins only when non-empty).
`D1Client`/`S3Storage` read it **per operation** → edits apply on the next request
**with no restart** (S3 client cached, rebuilt only when relevant fields change).

- Settings file may hold secrets → `.gitignore`d (`App_Data/`). Mount a volume to
  persist dashboard edits; otherwise it falls back to env vars.

**First-run bootstrap (chicken-and-egg):** `POST /api/admin/setup` writes to D1, so
D1 must be reachable via env defaults **once** to create the admin. After that the
admin can edit storage (and even D1) creds. If invalid D1 creds are saved, login
fails until fixed — repair via env vars or delete `App_Data/settings.json` to fall
back to env.

### Provisioning (one-time)

```bash
wrangler d1 create tv-video-hub      # → database_id  (D1 token: My Profile → API Tokens → "D1 Edit")
# Object storage: create two buckets + S3 access key/secret on your provider
#   R2:    wrangler r2 bucket create videos && wrangler r2 bucket create apks
#   AWS:   aws s3 mb s3://videos && aws s3 mb s3://apks
```

Tables are auto-created on first run; no manual migration step.

## Run locally

```bash
cd backend
dotnet user-secrets init --project MediaHub.Api
dotnet user-secrets set "Cloudflare:AccountId" "…" --project MediaHub.Api
# … set the rest …
dotnet run --project MediaHub.Api
# → http://localhost:5080/api/health  and  /openapi/v1.json
```

## Run in Docker

```bash
cd backend
cp .env.example .env   # fill in CF_*, STORAGE_*, API_KEY
docker compose up -d --build
# → http://localhost:8080/api/health
```

## 📝 Notes on S3 presigning

- `Storage__UseChecksumWhenRequired=true` → `RequestChecksumCalculation/ResponseChecksumValidation = WHEN_REQUIRED`.
- `Storage__DisablePayloadSigning=true` → uploads skip streaming SigV4.
- These defaults keep R2/MinIO presigned URLs clean and ExoPlayer-streamable. Strict
  AWS setups can flip both off. First thing to check on `SignatureDoesNotMatch`.
- `ServiceUrl` set → custom endpoint + `AuthenticationRegion`; empty → AWS regional
  endpoint via `RegionEndpoint.GetBySystemName(Region)`.
