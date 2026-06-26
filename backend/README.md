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
├── Program.cs               composition root, DI, JSON, startup schema init
├── Options/                 CloudflareOptions, ApiOptions
├── Data/
│   ├── D1Client.cs          HTTP client for the D1 query API (parameterized SQL)
│   ├── DatabaseInitializer.cs   ensures tables exist at boot
│   ├── VideoRepository.cs
│   └── AppReleaseRepository.cs
├── Storage/R2Storage.cs     presigned GET URLs + uploads against R2
├── Auth/ApiKeyFilter.cs     X-Api-Key guard for write endpoints
├── Endpoints/
│   ├── VideoEndpoints.cs    GET /api/videos, GET /api/videos/{id}, POST /api/videos
│   └── AppEndpoints.cs      GET /api/app/latest|download, POST /api/app/releases
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

\* If `Api__Key` is empty, write endpoints return `503` (fail-closed).

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
