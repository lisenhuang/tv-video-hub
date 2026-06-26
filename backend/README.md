# MediaHub backend (.NET 10)

ASP.NET Core minimal API that serves the video catalog to the Android TV app,
generates short-lived streaming URLs, and hosts APK builds for self-update.

- 🚀 **Zero-env setup.** Boots with **no environment variables and no config**, then is
  configured entirely in the **`/admin`** dashboard. Everything (admin account, database,
  storage, release key) persists to a local file (`App_Data/settings.json`).
- 🗄️ **Pluggable database.** Cloudflare **D1** (HTTP API) **or** a self-hosted SQL DB
  via EF Core: **SQLite / PostgreSQL / SQL Server** (MySQL selectable — see note below).
  Schema auto-created (D1 `CREATE TABLE IF NOT EXISTS`; EF `EnsureCreated()`), additive.
- 📦 **Object storage:** any **S3-compatible** store (R2, AWS S3, MinIO, Backblaze B2, …)
  via `AWSSDK.S3`. Videos/APKs stream/download via presigned URLs. R2-ready by default.

See the [repo root README](../README.md) for the full HTTP/JSON contract.

## Project layout

```
MediaHub.Api/
├── Program.cs               composition root, DI, JSON, auth, lazy schema init
├── Options/                 CloudflareOptions (D1 seed), StorageOptions, DatabaseOptions, ApiOptions, SettingsOptions
├── Data/
│   ├── D1Client.cs          HTTP client for the D1 query API (parameterized SQL)
│   ├── IRepositories.cs     IVideoRepository / IAppReleaseRepository / ISchemaInitializer
│   ├── DatabaseService.cs   resolves the impl for the configured provider (+ lazy schema)
│   ├── RepositoryFacades.cs VideoRepository / AppReleaseRepository (ensure schema → delegate)
│   ├── AdminRepository.cs   single admin, stored LOCALLY (no DB needed to log in)
│   ├── VideoCreationService.cs   shared create/delete logic (public + admin)
│   ├── D1/                  D1VideoRepository, D1AppReleaseRepository, D1SchemaInitializer
│   └── Ef/                  MediaHubDbContext, EfContextFactory, Ef{Video,AppRelease}Repository, EfSchemaInitializer
├── Settings/                local-file config (single source of bootstrap truth, live-reloaded)
│   ├── PersistedSettings.cs        Admin + Database + Storage + Api
│   ├── SettingsStore.cs            read/write the JSON file
│   ├── EffectiveDatabaseConfig.cs  resolved DB snapshot (provider + creds)
│   ├── EffectiveStorageConfig.cs   resolved S3 storage snapshot
│   └── SettingsProvider.cs         singleton; live source for D1Client/EF/S3Storage/ApiKey
├── Storage/S3Storage.cs     presigned GET URLs + upload/delete/exists against any S3 store
├── Auth/
│   ├── ApiKeyFilter.cs      X-Api-Key guard (key read live from settings)
│   └── PasswordHasher.cs    PBKDF2/SHA-256 (framework crypto, no packages)
├── Endpoints/
│   ├── VideoEndpoints.cs    GET /api/videos, GET /api/videos/{id}, POST /api/videos
│   ├── AppEndpoints.cs      GET /api/app/latest|latest.apk|download, POST /api/app/releases
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
| GET    | `/api/app/latest.apk`          | —           | 302 → presigned **latest** APK (fixed path) |
| GET    | `/api/app/download?versionCode`| —           | 302 → presigned APK URL (latest if omitted) |
| POST   | `/api/app/releases`            | `X-Api-Key` | publish a build (used by CI)         |

### Admin dashboard (`/admin`)

Vanilla HTML/CSS/JS under `wwwroot/admin/` (light/dark theme). **Cookie auth** — an
*additional* path that doesn't affect public endpoints or the `X-Api-Key` write path.
First-run wizard: **(1)** create the single admin (stored locally, no DB needed) →
**(2)** configure database + object storage + release key → **(3)** manage videos.
Single-admin: after the first admin exists, setup is permanently closed.

| Method | Route                          | Auth     | Purpose                                   |
|--------|--------------------------------|----------|-------------------------------------------|
| GET    | `/admin`                       | —        | serves the dashboard SPA                  |
| GET    | `/api/admin/setup-state`       | —        | `{ needsAdmin, authenticated, needsDatabase, needsStorage }` (`needsSetup` kept as alias) |
| POST   | `/api/admin/setup`             | —¹       | create the first admin + sign in (409 if one exists) |
| POST   | `/api/admin/login`             | —        | sign in (cookie); 401 on bad creds        |
| POST   | `/api/admin/logout`            | cookie   | sign out                                  |
| GET    | `/api/admin/me`                | cookie   | `{ username }`                            |
| POST   | `/api/admin/change-password`   | cookie   | change the existing admin's password      |
| GET    | `/api/admin/videos`            | cookie   | list catalog (503 if DB not configured)   |
| POST   | `/api/admin/videos`            | cookie   | add a video (multipart upload OR JSON ref)|
| DELETE | `/api/admin/videos/{id}`       | cookie   | delete the DB row **and** the storage object |
| GET    | `/api/admin/releases`          | cookie   | list app releases (read-only)             |
| GET    | `/api/admin/settings`          | cookie   | effective DB + storage + key config, **secrets masked** |
| PUT    | `/api/admin/settings`          | cookie   | edit config (blank secret = keep current) |
| POST   | `/api/admin/settings/test`     | cookie   | probe DB (schema + read) + storage (list) |

¹ `setup` is public but only succeeds while there is **no** admin.

The dashboard never receives full secrets — settings return only `{ isSet, last4 }`
per secret. Saving a blank secret field leaves the stored value unchanged.

## ⚙️ Configuration

**You don't have to set anything** — boot the app and configure it at `/admin`. The
local file `App_Data/settings.json` is the source of truth. Env vars/appsettings are
**OPTIONAL seeds**: a persisted (dashboard) value always wins over a non-empty seed.
All config is live-reloaded (read per operation; no restart).

**🗄️ Database — pluggable (`Database__*` + D1 fields from `Cloudflare__*`)**

| variable                     | notes                                                       |
|------------------------------|-------------------------------------------------------------|
| `Database__Provider`         | `d1` · `sqlite` · `postgres` · `mysql` · `sqlserver` (empty = setup) |
| `Database__ConnectionString` | for the SQL providers                                       |
| `Cloudflare__AccountId` / `Cloudflare__D1__DatabaseId` / `Cloudflare__D1__ApiToken` | D1 only (token = **D1 Edit**) |

- **D1** → SQLite over the Cloudflare HTTP API. **SQLite/Postgres/SQL Server** → EF Core.
- **MySQL** is selectable, but no `Pomelo.EntityFrameworkCore.MySql` build targets EF
  Core 10 yet, so its provider is **not bundled** — selecting it errors until a
  compatible Pomelo package is added and `builder.UseMySql(...)` is wired in
  `EfContextFactory`.
- SQLite connection example: `Data Source=App_Data/mediahub.db` (persisted with the
  `App_Data` volume).

**📦 Object storage — S3-compatible (`Storage__*`)**

| variable                          | notes                                            |
|-----------------------------------|--------------------------------------------------|
| `Storage__ServiceUrl`             | S3 endpoint; **empty = AWS regional endpoint**   |
| `Storage__Region`                 | `auto` (R2) · `us-east-1` (AWS) · region (MinIO) |
| `Storage__AccessKeyId` / `Storage__SecretAccessKey` | S3 credentials                 |
| `Storage__VideoBucket` / `Storage__ApkBucket` | default `videos` / `apks`            |
| `Storage__ForcePathStyle`         | default `true` (R2/MinIO); AWS virtual-hosted → `false` |
| `Storage__PresignTtlMinutes`      | default `360`                                    |
| `Storage__DisablePayloadSigning`  | default `true` (R2 needs it)                     |
| `Storage__UseChecksumWhenRequired`| default `true` (clean R2 presigns)               |

**Other**

| variable             | notes                                                   |
|----------------------|---------------------------------------------------------|
| `Api__Key`           | release `X-Api-Key` secret (writes `503` until set)     |
| `Settings__FilePath` | local settings file; default `App_Data/settings.json`   |

**Storage provider presets:**

| Provider | `ServiceUrl`                              | `Region`    | `ForcePathStyle` |
|----------|-------------------------------------------|-------------|------------------|
| R2       | `https://<acct>.r2.cloudflarestorage.com` | `auto`      | `true`           |
| AWS S3   | *(empty)*                                 | `us-east-1` | `false`          |
| MinIO    | `http://minio:9000`                       | `us-east-1` | `true`           |

### 🔄 How provider switching + schema init work

- `SettingsProvider` exposes the **effective** Database/Storage/ApiKey, live.
- `DatabaseService` (scoped) picks the impl for the **current** provider:
  D1 → `D1*Repository` (HTTP); SQL → `Ef*Repository` over `EfContextFactory` (which
  caches `DbContextOptions` per provider+connection, rebuilt on change).
- Schema is ensured **lazily** on first use (and at startup if already configured):
  D1 `CREATE TABLE IF NOT EXISTS`; EF `EnsureCreated()` — additive, never destructive.
  Until the DB is configured, catalog endpoints return a clean **503** (not a crash).

### First-run (no chicken-and-egg)

The admin lives in the **local file**, so the very first run needs **no database**.
Create the admin at `/admin`, then configure DB + storage + release key — all in the UI.

## Run locally

```bash
cd backend
dotnet run --project MediaHub.Api      # zero config needed
# → http://localhost:5080/api/health, then open /admin to set everything up
```

## Run in Docker

```bash
cd backend
docker compose up -d --build           # no .env required; App_Data is a named volume
# → http://localhost:8080/api/health, then open http://localhost:8080/admin
```

## 📝 Notes on S3 presigning

- `Storage__UseChecksumWhenRequired=true` → `RequestChecksumCalculation/ResponseChecksumValidation = WHEN_REQUIRED`.
- `Storage__DisablePayloadSigning=true` → uploads skip streaming SigV4.
- These defaults keep R2/MinIO presigned URLs clean and ExoPlayer-streamable. Strict
  AWS setups can flip both off. First thing to check on `SignatureDoesNotMatch`.
- `ServiceUrl` set → custom endpoint + `AuthenticationRegion`; empty → AWS regional
  endpoint via `RegionEndpoint.GetBySystemName(Region)`.
