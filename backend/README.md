# MediaHub backend (.NET 10)

ASP.NET Core minimal API that serves the video catalog to the Android TV app,
generates short-lived streaming URLs, and hosts APK builds for self-update.

- **Database:** Cloudflare **D1** (SQLite) via the D1 HTTP query API — no EF Core,
  no local DB file. Schema is created automatically at startup
  (`CREATE TABLE IF NOT EXISTS`, additive only).
- **Storage:** Cloudflare **R2** (S3-compatible) via `AWSSDK.S3`. Videos and APKs
  live in R2; clients stream/download through presigned URLs.

See the [repo root README](../README.md) for the full HTTP/JSON contract.

## Project layout

```
MediaHub.Api/
├── Program.cs               composition root, DI, JSON, auth, startup schema init
├── Options/                 CloudflareOptions, ApiOptions, SettingsOptions
├── Data/
│   ├── D1Client.cs          HTTP client for the D1 query API (parameterized SQL)
│   ├── DatabaseInitializer.cs   ensures tables exist at boot (videos, app_releases, admins)
│   ├── VideoRepository.cs
│   ├── VideoCreationService.cs   shared create/delete logic (public + admin)
│   ├── AppReleaseRepository.cs
│   └── AdminRepository.cs
├── Settings/                runtime-editable Cloudflare config
│   ├── CloudflareSettings.cs        persisted override shape
│   ├── SettingsStore.cs             read/write the JSON file
│   ├── EffectiveCloudflareConfig.cs resolved (overrides ∪ defaults) snapshot
│   └── CloudflareSettingsProvider.cs  singleton; live source for D1Client/R2Storage
├── Storage/R2Storage.cs     presigned GET URLs + uploads/deletes against R2
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

A self-contained dashboard (vanilla HTML/CSS/JS under `wwwroot/admin/`, light/dark
theme) for managing the catalog and Cloudflare config from a browser. It uses
**cookie authentication** — an *additional* auth path that does not affect the
public endpoints or the `X-Api-Key` write path. Single-admin model: the first run
creates exactly one admin via a setup form; after that, setup is permanently closed.

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
| DELETE | `/api/admin/videos/{id}`       | cookie   | delete the D1 row **and** the R2 object   |
| GET    | `/api/admin/releases`          | cookie   | list app releases (read-only)             |
| GET    | `/api/admin/settings`          | cookie   | effective Cloudflare config, **secrets masked** |
| PUT    | `/api/admin/settings`          | cookie   | edit config (blank secret = keep current) |
| POST   | `/api/admin/settings/test`     | cookie   | probe D1 (`SELECT 1`) + R2 (list)         |

¹ `setup` is public but only succeeds while there are **zero** admins.

The dashboard never receives full secrets — `GET/PUT /api/admin/settings` return only
`{ isSet, last4 }` per secret. Saving a blank secret field leaves the stored value
unchanged.

## Configuration

Bind from environment variables (double underscore = nesting) or
`dotnet user-secrets` in development. **Never commit real values.**

| variable                            | required | notes                                  |
|-------------------------------------|----------|----------------------------------------|
| `Cloudflare__AccountId`             | yes      | account id from the dashboard          |
| `Cloudflare__D1__DatabaseId`        | yes      | D1 database id                         |
| `Cloudflare__D1__ApiToken`          | yes      | API token, **D1 Edit** permission      |
| `Cloudflare__R2__AccessKeyId`       | yes      | R2 S3 access key id                    |
| `Cloudflare__R2__SecretAccessKey`   | yes      | R2 S3 secret                           |
| `Cloudflare__R2__VideoBucket`       | no       | default `videos`                       |
| `Cloudflare__R2__ApkBucket`         | no       | default `apks`                         |
| `Cloudflare__R2__ServiceUrl`        | no       | derived from account id if blank       |
| `Cloudflare__R2__PresignTtlMinutes` | no       | default `360`                          |
| `Api__Key`                          | yes*     | required to enable write endpoints     |
| `Settings__FilePath`                | no       | runtime settings file; default `App_Data/cloudflare.settings.json` |

\* If `Api__Key` is empty, write endpoints return `503` (fail-closed).

### Runtime-editable Cloudflare config (dashboard)

The env vars above are the **bootstrap defaults**. Once an admin is logged in, the
Cloudflare R2/D1 config can be viewed and edited in the dashboard and is persisted to
a writable JSON file (`Settings__FilePath`, default `App_Data/cloudflare.settings.json`
under the content root; the directory is created automatically). The effective config
is **the persisted file merged over the env/appsettings defaults** — a persisted field
wins only when non-empty, otherwise the default is used. `D1Client` and `R2Storage`
read this effective config **per operation**, so edits take effect on the next request
**without a restart** (the R2 S3 client is cached and rebuilt only when its relevant
fields change).

The settings file may contain secrets — it is `.gitignore`d (`App_Data/`). Back it up
or mount it on a persistent volume if you rely on dashboard-set values; otherwise the
app falls back to the env vars.

#### First-run admin bootstrap (chicken-and-egg)

Creating the first admin (`POST /api/admin/setup`) writes a row to D1, which requires a
**working D1 connection**. So D1 must be reachable via the env-var defaults at least
once to bootstrap the admin. After that, the logged-in admin can edit R2 credentials
(and even the D1 settings) from the dashboard. If you change the D1 credentials in the
dashboard to something invalid, login/setup will fail until valid D1 settings are
restored — either fix them via env vars (which the persisted file overrides only when
non-empty) or delete/repair `App_Data/cloudflare.settings.json` to fall back to env.

### Provisioning Cloudflare (one-time)

```bash
# install wrangler, then:
wrangler d1 create tv-video-hub           # → copy the database_id
wrangler r2 bucket create videos
wrangler r2 bucket create apks
# Create an R2 API token (Account → R2 → Manage API tokens) → access key id + secret.
# Create a D1 API token (My Profile → API Tokens → "D1 Edit") for Cloudflare__D1__ApiToken.
```

The app creates the tables itself on first run; no manual migration step.

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
cp .env.example .env   # fill in CF_* and API_KEY
docker compose up -d --build
# → http://localhost:8080/api/health
```

## Notes on R2 presigning

R2 doesn't implement S3's newer flexible-checksum / streaming-trailer features, so
the client is configured with `RequestChecksumCalculation = WHEN_REQUIRED` and
uploads use `DisablePayloadSigning = true`. This keeps presigned URLs clean and
streamable by ExoPlayer. If you swap SDK versions and presigned URLs start failing
with `SignatureDoesNotMatch`, that setting is the first thing to check.
