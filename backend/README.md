# MediaHub backend (.NET 10)

ASP.NET Core minimal API that serves the video catalog to the Android TV app and
generates short-lived streaming URLs. It also **ships the current release APK inside the
image** — a single committed file at `wwwroot/app/app-release.apk`, served directly as a
static download at `/app/app-release.apk`. `GET /api/app/latest` returns the version
metadata and points the app at that file (see "App self-update" below).

- 🚀 **Zero-env setup.** Boots with **no environment variables and no config**, then is
  configured in the **`/admin`** dashboard.
- 💾 **Only the DB connection is on disk** (`App_Data/db.json`). The **admin account,
  object-storage config, and release API key all live IN THE DATABASE** (tables
  `admins`, `app_config`) — so a fresh container with the same DB connection has
  everything.
- 🗄️ **Pluggable database.** Cloudflare **D1** (HTTP API) **or** a self-hosted SQL DB
  via EF Core: **SQLite / PostgreSQL / SQL Server** (MySQL selectable — see note below).
  Schema auto-created (D1 `CREATE TABLE IF NOT EXISTS`; EF `EnsureCreated()`), additive.
- 📦 **Object storage:** any **S3-compatible** store (R2, AWS S3, MinIO, Backblaze B2, …)
  via `AWSSDK.S3`, **or Local disk** (the server's own filesystem). Videos/APKs
  stream/download via short-lived presigned/signed URLs. R2-ready by default; local
  serves at `/api/media/…` with HMAC-signed, range-capable URLs.

See the [repo root README](../README.md) for the full HTTP/JSON contract.

### What's stored where

| Lives on local disk (`App_Data/db.json`) | Lives in the database |
|------------------------------------------|-----------------------|
| Database connection: provider + D1 creds **or** connection string | `admins` (PBKDF2), `app_config` (storage config + release API key), `videos`, `app_releases` |

## Project layout

```
MediaHub.Api/
├── Program.cs               composition root, DI, JSON, auth, lazy schema init
├── Options/                 CloudflareOptions (D1 seed), DatabaseOptions, SettingsOptions
├── Data/
│   ├── D1Client.cs          HTTP client for the D1 query API (parameterized SQL)
│   ├── IRepositories.cs     IVideo/IAppRelease/IAdmin/IAppConfigRepository, ISchemaInitializer
│   ├── DatabaseService.cs   resolves the impl for the configured provider (+ CanConnect/lazy schema)
│   ├── RepositoryFacades.cs VideoRepository / AppReleaseRepository (ensure schema → delegate)
│   ├── AdminRepository.cs   DB-backed admin facade
│   ├── VideoCreationService.cs   shared create/delete logic (public + admin)
│   ├── D1/                  D1{Video,AppRelease,Admin,AppConfig}Repository, D1SchemaInitializer
│   └── Ef/                  MediaHubDbContext, EfContextFactory, Ef{Video,AppRelease,Admin,AppConfig}Repository, EfSchemaInitializer
├── Settings/
│   ├── DatabaseFileConfig.cs       the ONLY on-disk config shape (DB connection)
│   ├── DbConfigStore.cs            read/write App_Data/db.json
│   ├── SettingsProvider.cs         singleton; live DB-connection config (file + env seed)
│   ├── AppConfigProvider.cs        singleton; storage + api-key read FROM THE DB (cached)
│   ├── EffectiveDatabaseConfig.cs  resolved DB snapshot (provider + creds)
│   └── EffectiveStorageConfig.cs   resolved storage snapshot (provider + S3/local fields)
├── Storage/                 IObjectStorage: S3Storage + LocalStorage, picked per-call by StorageRouter
├── Auth/
│   ├── ApiKeyFilter.cs      X-Api-Key guard (key read live from the DB)
│   └── PasswordHasher.cs    PBKDF2/SHA-256 (framework crypto, no packages)
├── Endpoints/
│   ├── VideoEndpoints.cs    GET /api/videos, GET /api/videos/{id}, POST /api/videos
│   ├── AppEndpoints.cs      GET /api/app/latest — version metadata (downloadUrl → /app/app-release.apk)
│   ├── MediaEndpoints.cs    GET /api/media/{bucket}/{**key} — signed, range-capable local serving
│   └── AdminEndpoints.cs    cookie-authed /api/admin/* dashboard API
├── wwwroot/admin/           the static admin dashboard (index.html + app.js + styles.css)
├── wwwroot/app/             the committed release APK (app-release.apk), served at /app/app-release.apk
└── Models/                  entities + DTOs
```

## Endpoints

| Method | Route                          | Auth        | Purpose                              |
|--------|--------------------------------|-------------|--------------------------------------|
| GET    | `/api/health`                  | —           | liveness                             |
| GET    | `/api/videos`                  | —           | list catalog                         |
| GET    | `/api/videos/{id}`             | —           | details + presigned `playbackUrl`    |
| POST   | `/api/videos`                  | `X-Api-Key` | register/upload a video              |
| GET    | `/api/app/latest`              | —           | newest APK metadata (204 if none); `downloadUrl` → this backend's `/app/app-release.apk` |
| GET    | `/app/app-release.apk`         | —           | the committed release APK, served directly as a static file (universal: arm v7 + v8) |
| GET    | `/api/media/{bucket}/{**key}`  | signed URL  | serve a local-storage object (HMAC `sig`+`exp`, range-capable) |

### Admin dashboard (`/admin`)

Vanilla HTML/CSS/JS under `wwwroot/admin/` (light/dark theme). **Cookie auth** — an
*additional* path that doesn't affect public endpoints or the `X-Api-Key` write path.
First-run wizard, **strict order**: **(1)** connect a **database** → **(2)** create the
single **admin** (in the DB) → **(3)** configure **object storage + release key** (in
the DB) → manage videos. Single-admin: after the first admin exists, setup is closed.

| Method | Route                          | Auth     | Purpose                                   |
|--------|--------------------------------|----------|-------------------------------------------|
| GET    | `/admin`                       | —        | serves the dashboard SPA                  |
| GET    | `/api/admin/setup-state`       | —        | `{ needsDatabase, needsAdmin, needsStorage, authenticated }` (`needsSetup` = needsAdmin alias) |
| GET    | `/api/admin/db-config`         | —²       | bootstrap: current DB connection (masked) |
| PUT    | `/api/admin/db-config`         | —²       | bootstrap: save DB connection + report `connects` |
| POST   | `/api/admin/setup`             | —¹       | create the first admin (in DB) + sign in  |
| POST   | `/api/admin/login`             | —        | sign in (cookie); needs the DB up; 401 on bad creds |
| POST   | `/api/admin/logout`            | cookie   | sign out                                  |
| GET    | `/api/admin/me`                | cookie   | `{ username }`                            |
| POST   | `/api/admin/change-password`   | cookie   | change the existing admin's password      |
| GET    | `/api/admin/videos`            | cookie   | list catalog (503 if DB not configured)   |
| POST   | `/api/admin/videos`            | cookie   | add a video (multipart upload OR JSON ref)|
| DELETE | `/api/admin/videos/{id}`       | cookie   | delete the DB row **and** the storage object |
| GET    | `/api/admin/releases`          | cookie   | list app releases (read-only)             |
| GET    | `/api/admin/settings`          | cookie   | effective DB + storage + key config, **secrets masked** |
| PUT    | `/api/admin/settings`          | cookie   | edit config (DB→file; storage/key→DB; blank secret = keep) |
| POST   | `/api/admin/settings/test`     | cookie   | probe DB (schema + read) + storage (list) |

¹ `setup` is public but only succeeds while the DB connects and there is **no** admin.
² `db-config` is public only **while no admin exists** (bootstrap); afterwards use the
   authed `/settings`.

The dashboard never receives full secrets — settings return only `{ isSet, last4 }`
per secret. Saving a blank secret field leaves the stored value unchanged.

## ⚙️ Configuration

**You don't have to set anything** — boot the app and configure it at `/admin`. Only the
**database connection** is on disk (`App_Data/db.json`); the admin account, storage
config, and release key live **in the database**. Env vars can OPTIONALLY seed the DB
connection (a dashboard value wins). All config is live-reloaded (no restart).

### 🔶 First run: connect Cloudflare D1

On first launch the dashboard opens on **Step 1 — Connect a database** (the same steps are
shown inline when you pick **Cloudflare D1**):

1. **Create the database** — Cloudflare dashboard → **Storage & Databases → D1 SQL Database
   → Create**. Open it and copy the **Database ID**.  *(CLI: `wrangler d1 create tv-video-hub`.)*
2. **Account ID** — Cloudflare dashboard right sidebar, or the hex in
   `dash.cloudflare.com/<account-id>`.
3. **API Token** — profile menu → **My Profile → API Tokens → Create Token → Create Custom
   Token**, add permission **Account › D1 › Edit**, create, and copy the token.
4. In the dashboard pick **Cloudflare D1**, paste **Account ID / D1 Database ID / D1 API
   Token**, and click **Test & continue**. Tables are created automatically on first connect.

Then create the admin (Step 2) and configure object storage — Cloudflare **R2** or any
S3-compatible store — plus the release key (Step 3). Docs: <https://developers.cloudflare.com/d1/get-started/>.

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

| variable             | notes                                                   |
|----------------------|---------------------------------------------------------|
| `Settings__FilePath` | on-disk DB-config file; default `App_Data/db.json`      |

**📦 Object storage + 🔑 release API key — configured at `/admin` (stored in the DB).**
Not env vars. Provider presets (set them in the dashboard's Storage form):

| Provider | Service URL                               | Region      | Force path-style |
|----------|-------------------------------------------|-------------|------------------|
| R2       | `https://<acct>.r2.cloudflarestorage.com` | `auto`      | on               |
| AWS S3   | *(empty)*                                 | `us-east-1` | off              |
| MinIO    | `http://minio:9000`                       | `us-east-1` | on               |

R2-safe defaults (toggleable in the form): force-path-style **on**, disable upload
payload-signing **on**, checksums **only when required** — these keep R2/MinIO presigned
URLs clean and ExoPlayer-streamable; strict AWS setups can flip them. With `ServiceUrl`
set the SDK uses that endpoint + `AuthenticationRegion`; empty → AWS regional endpoint
via `RegionEndpoint.GetBySystemName(Region)`.

**💾 Local disk (no S3).** In the Storage form set **Storage provider → Local disk** and a
**Local media directory** (default `App_Data/media`). Objects are stored at
`{dir}/{bucket}/{key}` (the video/APK *bucket* names become subdirectories) and served by
the backend itself at `GET /api/media/{bucket}/{**key}?exp=&sig=`:

- **Short-lived HMAC-signed URLs** — `sig = base64url(HMACSHA256(localSigningKey,
  "{bucket}/{key}|{exp}"))`, `exp` = unix seconds; the endpoint validates the signature
  (constant-time) and expiry → else **403**. TTL = the storage TTL (min). The signing key
  is **server-managed** (auto-generated 32 random bytes on first use, stored in
  `app_config`, never shown in settings).
- **HTTP range** support (`enableRangeProcessing`) so ExoPlayer can seek.
- **Path-traversal guard** — keys are resolved under the base dir and rejected if they
  escape it (no `..`, no rooted paths).
- The app needs **no change**: `playbackUrl` / APK download URLs are just absolute URLs
  pointing at this backend instead of S3.
- **Persist the media directory.** `App_Data/media` sits under the existing `App_Data`
  volume, but media gets large — a dedicated mount/volume is wise. Don't lose it on
  redeploy.

### 🔄 How config + provider switching + schema init work

- **DB connection** comes from `App_Data/db.json` (or an env seed) via `SettingsProvider`.
- **Storage + release key** come from the **`app_config`** table via `AppConfigProvider`
  (a singleton that reads the DB through a scope, caches a snapshot, and reloads on save).
  `S3Storage` / `ApiKeyFilter` read it per request → dashboard edits apply with no restart.
- `DatabaseService` (scoped) picks the impl for the **current** provider:
  D1 → `D1*Repository` (HTTP); SQL → `Ef*Repository` over `EfContextFactory` (caches
  `DbContextOptions` per provider+connection, rebuilt on change).
- Schema is ensured **lazily** on first use (and at startup if already configured):
  D1 `CREATE TABLE IF NOT EXISTS` (`videos`, `app_releases`, `admins`, `app_config`);
  EF `EnsureCreated()` — additive, never destructive. Until the DB connects, catalog +
  storage endpoints return a clean **503** (not a crash).

### First-run order (DB first)

The admin now lives in the **database**, so step 1 is **connect a database**
(`/admin` shows a DB form + Test). Then create the admin (in the DB), then configure
storage + release key (in the DB). Login requires the DB to be up.

## Run locally

```bash
cd backend
dotnet run --project MediaHub.Api      # zero config needed
# → http://localhost:5080/api/health, then open /admin (step 1: connect a database)
```

## Run in Docker

Easiest via the **Makefile** (wraps `docker compose`):

```bash
cd backend
make up        # build + start in the background → http://localhost:8080/admin
make logs      # follow logs        make ps     # status + ports
make health    # curl /api/health   make down   # stop (keeps your DB config)
make help      # list all targets
```

Or use compose directly:

```bash
cd backend
docker compose up -d --build           # no .env required; App_Data is a named volume
# → http://localhost:8080/api/health, then open http://localhost:8080/admin
```

> 💾 **Stateless upgrades:** only `App_Data/db.json` is on disk. Persist the `App_Data`
> volume (compose already does) **or** seed the DB connection via env, and a brand-new
> image/container reuses the same database — and therefore the same admin, storage, and
> all data, which live in the DB.
