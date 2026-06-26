# MediaHub backend (.NET 10)

ASP.NET Core minimal API that serves the video catalog to the Android TV app,
generates short-lived streaming URLs, and hosts APK builds for self-update.

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
  via `AWSSDK.S3`. Videos/APKs stream/download via presigned URLs. R2-ready by default.

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
│   └── EffectiveStorageConfig.cs   resolved S3 storage snapshot
├── Storage/S3Storage.cs     presigned GET URLs + upload/delete/exists (config from the DB)
├── Auth/
│   ├── ApiKeyFilter.cs      X-Api-Key guard (key read live from the DB)
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

```bash
cd backend
docker compose up -d --build           # no .env required; App_Data is a named volume
# → http://localhost:8080/api/health, then open http://localhost:8080/admin
```

> 💾 **Stateless upgrades:** only `App_Data/db.json` is on disk. Persist the `App_Data`
> volume (compose already does) **or** seed the DB connection via env, and a brand-new
> image/container reuses the same database — and therefore the same admin, storage, and
> all data, which live in the DB.
