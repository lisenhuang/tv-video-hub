using System.Security.Claims;
using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Models;
using MediaHub.Api.Settings;
using MediaHub.Api.Storage;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MediaHub.Api.Endpoints;

/// <summary>
/// Cookie-authenticated admin dashboard API under <c>/api/admin</c>. ADDITIONAL auth
/// path; does not touch the public endpoints or the X-Api-Key write path.
///
/// Persistence split: ONLY the database connection lives on disk; the admin account,
/// object-storage config, and the release API key all live IN THE DATABASE. So the
/// first-run wizard is strictly ordered: <b>configure the database first</b>, then
/// create the admin (in the DB), then configure storage + release key (in the DB).
/// Single-admin model: setup creates exactly one admin, then is closed.
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("admin");

        // ---- Auth lifecycle --------------------------------------------------

        // GET /api/admin/setup-state — public. Drives the first-run wizard in strict
        // order: needsDatabase → needsAdmin → needsStorage. Never throws on a
        // misconfigured/unreachable DB (collapses to needsDatabase).
        group.MapGet("/setup-state", async (
            HttpContext http, DatabaseService db, AppConfigProvider appConfig, CancellationToken ct) =>
        {
            var authed = http.User.Identity?.IsAuthenticated == true;

            var dbConnects = await db.CanConnectAsync(ct);
            if (!dbConnects)
            {
                // Step 1: configure the database. Admin/storage can't be checked yet.
                return Results.Ok(new SetupStateDto(
                    NeedsSetup: true, NeedsAdmin: true, Authenticated: authed,
                    NeedsDatabase: true, NeedsStorage: true));
            }

            bool needsAdmin;
            try { needsAdmin = await db.Admins.CountAsync(ct) == 0; }
            catch { return DbDownState(authed); }

            var storage = await appConfig.GetStorageAsync(ct);
            var needsStorage = !StorageConfigured(storage);

            return Results.Ok(new SetupStateDto(
                NeedsSetup: needsAdmin,
                NeedsAdmin: needsAdmin,
                Authenticated: authed,
                NeedsDatabase: false,
                NeedsStorage: needsStorage));
        });

        // GET /api/admin/db-config — bootstrap-only (public WHILE no admin exists).
        // Returns the current on-disk DB config (secrets masked) so the wizard's
        // step-1 form can show what's set. Once an admin exists, use authed /settings.
        group.MapGet("/db-config", async (
            SettingsProvider settings, DatabaseService db, CancellationToken ct) =>
        {
            if (await AdminAlreadyExists(db, ct))
                return Results.Json(new { error = "setup is closed; sign in to edit settings." },
                    statusCode: StatusCodes.Status403Forbidden);
            return Results.Ok(DbConfigView(settings));
        });

        // PUT /api/admin/db-config — bootstrap-only (public WHILE no admin exists).
        // Saves the database connection to the local file and reports whether it
        // connects. This is the FIRST wizard step; everything else needs the DB up.
        group.MapPut("/db-config", async (
            SettingsUpdateRequest body, SettingsProvider settings, DatabaseService db, CancellationToken ct) =>
        {
            if (await AdminAlreadyExists(db, ct))
                return Results.Json(new { error = "setup is closed; sign in to edit settings." },
                    statusCode: StatusCodes.Status403Forbidden);

            ApplyDbConfig(settings, body);
            var connects = await db.CanConnectAsync(ct);
            return Results.Ok(new { config = DbConfigView(settings), connects });
        });

        // POST /api/admin/setup — public, only succeeds when the DB is up and no admin
        // exists. The admin row is stored in the database.
        group.MapPost("/setup", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, DatabaseService db, CancellationToken ct) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Results.BadRequest(new { error = "username and password are required." });

            if (!await db.CanConnectAsync(ct))
                return Results.Json(new { error = "configure the database first." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            try
            {
                if (await admins.CountAsync(ct) > 0)
                    return Results.Conflict(new { error = "an admin already exists; setup is closed." });

                var (hash, salt) = hasher.Hash(body.Password);
                await admins.InsertAsync(new Admin
                {
                    Id = Guid.NewGuid().ToString("n"),
                    Username = username,
                    PasswordHash = hash,
                    PasswordSalt = salt,
                    CreatedAt = DateTimeOffset.UtcNow,
                }, ct);
            }
            catch (Exception)
            {
                // UNIQUE(username) race or a DB hiccup → treat as conflict/closed.
                return Results.Conflict(new { error = "an admin already exists; setup is closed." });
            }

            await SignInAsync(http, username);
            return Results.Ok(new AdminIdentityDto(username));
        });

        // POST /api/admin/login — public. Admin lives in the DB → DB must be up.
        group.MapPost("/login", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, DatabaseService db, CancellationToken ct) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Unauthorized();

            if (!await db.CanConnectAsync(ct))
                return Results.Json(new { error = "configure the database first." },
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var admin = await admins.GetByUsernameAsync(username, ct);
            if (admin is null || !hasher.Verify(body.Password, admin.PasswordHash, admin.PasswordSalt))
                return Unauthorized();

            await SignInAsync(http, username);
            return Results.Ok(new AdminIdentityDto(username));
        });

        // POST /api/admin/logout — auth.
        group.MapPost("/logout", async (HttpContext http) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();

        // GET /api/admin/me — auth.
        group.MapGet("/me", (HttpContext http) =>
            Results.Ok(new AdminIdentityDto(http.User.Identity?.Name ?? string.Empty)))
            .RequireAuthorization();

        // POST /api/admin/change-password — auth.
        group.MapPost("/change-password", async (
            ChangePasswordRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, CancellationToken ct) =>
        {
            var username = http.User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
            if (string.IsNullOrEmpty(body.CurrentPassword) || string.IsNullOrEmpty(body.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required." });

            var admin = await admins.GetByUsernameAsync(username, ct);
            if (admin is null || !hasher.Verify(body.CurrentPassword, admin.PasswordHash, admin.PasswordSalt))
                return Results.Json(new { error = "current password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);

            var (hash, salt) = hasher.Hash(body.NewPassword);
            await admins.UpdatePasswordAsync(username, hash, salt, ct);
            return Results.Ok(new { ok = true });
        }).RequireAuthorization();

        // ---- Video management (auth) ----------------------------------------

        var videos = group.MapGroup("/videos").RequireAuthorization();

        videos.MapGet("/", async (VideoRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct);
            var dto = new AdminVideoListDto(list.Select(VideoCreationService.ToSummary).ToList());
            return Results.Ok(dto);
        });

        videos.MapPost("/", async (HttpRequest request, VideoCreationService svc, CancellationToken ct) =>
        {
            if (request.HasFormContentType)
            {
                var form = await request.ReadFormAsync(ct);
                var result = await svc.CreateFromUploadAsync(form, ct);
                return result.Video is null
                    ? Results.BadRequest(new { error = result.Error })
                    : Results.Created($"/api/admin/videos/{result.Video.Id}", result.Video);
            }

            var body = await request.ReadFromJsonAsync<CreateVideoRequest>(ct);
            var refResult = await svc.CreateFromReferenceAsync(body, ct);
            return refResult.Video is null
                ? Results.BadRequest(new { error = refResult.Error })
                : Results.Created($"/api/admin/videos/{refResult.Video.Id}", refResult.Video);
        }).DisableAntiforgery();

        videos.MapDelete("/{id}", async (string id, VideoCreationService svc, CancellationToken ct) =>
        {
            var deleted = await svc.DeleteAsync(id, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        });

        // GET /api/admin/uploads/{id}/progress — poll the server→storage (R2/local)
        // transfer for an in-flight upload the dashboard tagged with this id. Lets the
        // progress bar show a real percentage during the otherwise-opaque "Processing"
        // phase. Best-effort + in-memory: an unknown id reports found=false / zeros.
        group.MapGet("/uploads/{id}/progress", (string id, UploadProgressTracker progress) =>
        {
            var s = progress.Get(id);
            return Results.Ok(new UploadProgressDto(s.Transferred, s.Total, s.Done, s.Failed, s.Found));
        }).RequireAuthorization();

        // ---- Settings: DB config (on disk) + storage/api key (in DB) (auth) -

        group.MapGet("/settings", async (
            SettingsProvider settings, AppConfigProvider appConfig, CancellationToken ct) =>
            Results.Ok(await MaskedViewAsync(settings, appConfig, ct)))
            .RequireAuthorization();

        group.MapPut("/settings", async (
            SettingsUpdateRequest body, SettingsProvider settings, AppConfigProvider appConfig,
            DatabaseService db, CancellationToken ct) =>
        {
            // 1) Database connection → the local file ONLY.
            ApplyDbConfig(settings, body);

            // 2) Storage + release key → the DATABASE (only if it connects).
            var storageOrKeyTouched = StorageOrKeyTouched(body);
            if (storageOrKeyTouched)
            {
                if (!await db.CanConnectAsync(ct))
                    return Results.Json(new { error = "configure & connect the database before saving storage/key." },
                        statusCode: StatusCodes.Status503ServiceUnavailable);

                var updates = new Dictionary<string, string>(StringComparer.Ordinal);
                // Provider selector ("s3" | "local"); normalize unknown values to "s3".
                if (body.StorageProvider is { } prov)
                    updates[AppConfigProvider.KeyStorageProvider] =
                        string.Equals(prov, "local", StringComparison.OrdinalIgnoreCase) ? "local" : "s3";
                Put(updates, AppConfigProvider.KeyStorageLocalBasePath, body.StorageLocalBasePath, allowBlank: false);
                Put(updates, AppConfigProvider.KeyStorageServiceUrl, body.StorageServiceUrl, allowBlank: true);
                Put(updates, AppConfigProvider.KeyStorageRegion, body.StorageRegion, allowBlank: true);
                Put(updates, AppConfigProvider.KeyStorageVideoBucket, body.StorageVideoBucket, allowBlank: true);
                Put(updates, AppConfigProvider.KeyStorageApkBucket, body.StorageApkBucket, allowBlank: true);
                if (body.StorageForcePathStyle is { } fps) updates[AppConfigProvider.KeyStorageForcePathStyle] = fps ? "true" : "false";
                if (body.StoragePresignTtlMinutes is { } ttl && ttl > 0) updates[AppConfigProvider.KeyStoragePresignTtlMinutes] = ttl.ToString();
                if (body.StorageDisablePayloadSigning is { } dps) updates[AppConfigProvider.KeyStorageDisablePayloadSigning] = dps ? "true" : "false";
                if (body.StorageUseChecksumWhenRequired is { } cwr) updates[AppConfigProvider.KeyStorageUseChecksumWhenRequired] = cwr ? "true" : "false";
                // Secrets: only when non-blank.
                if (!string.IsNullOrWhiteSpace(body.StorageAccessKeyId)) updates[AppConfigProvider.KeyStorageAccessKeyId] = body.StorageAccessKeyId;
                if (!string.IsNullOrWhiteSpace(body.StorageSecretAccessKey)) updates[AppConfigProvider.KeyStorageSecretAccessKey] = body.StorageSecretAccessKey;
                if (!string.IsNullOrWhiteSpace(body.ApiKey)) updates[AppConfigProvider.KeyApiKey] = body.ApiKey;

                if (updates.Count > 0)
                    await appConfig.SaveAsync(updates, ct);
            }

            return Results.Ok(await MaskedViewAsync(settings, appConfig, ct));
        }).RequireAuthorization();

        group.MapPost("/settings/test", async (
            DatabaseService db, StorageRouter storage, AppConfigProvider appConfig, CancellationToken ct) =>
        {
            var dbResult = await TestDatabaseAsync(db, ct);
            var storageResult = await TestStorageAsync(storage, appConfig, ct);
            return Results.Ok(new SettingsTestDto(dbResult, storageResult));
        }).RequireAuthorization();

        return app;
    }

    // ---- Helpers -------------------------------------------------------------

    private static IResult DbDownState(bool authed) => Results.Ok(new SetupStateDto(
        NeedsSetup: true, NeedsAdmin: true, Authenticated: authed,
        NeedsDatabase: true, NeedsStorage: true));

    private static IResult Unauthorized() =>
        Results.Json(new { error = "invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);

    private static async Task SignInAsync(HttpContext http, string username)
    {
        var claims = new[] { new Claim(ClaimTypes.Name, username) };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await http.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties { IsPersistent = true });
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static async Task<bool> AdminAlreadyExists(DatabaseService db, CancellationToken ct)
    {
        try { return await db.CanConnectAsync(ct) && await db.Admins.CountAsync(ct) > 0; }
        catch { return false; }
    }

    /// <summary>Apply the database-connection fields from the request to the on-disk file.</summary>
    private static void ApplyDbConfig(SettingsProvider settings, SettingsUpdateRequest body)
    {
        var file = settings.LoadFile();
        if (body.DatabaseProvider is not null)
        {
            var kind = EffectiveDatabaseConfig.ParseProvider(body.DatabaseProvider);
            file.Provider = kind == DatabaseProviderKind.None ? null : EffectiveDatabaseConfig.ProviderToString(kind);
        }
        if (body.AccountId is not null) file.AccountId = Blank(body.AccountId);
        if (body.D1DatabaseId is not null) file.D1DatabaseId = Blank(body.D1DatabaseId);
        if (!string.IsNullOrWhiteSpace(body.D1ApiToken)) file.D1ApiToken = body.D1ApiToken;
        if (!string.IsNullOrWhiteSpace(body.DatabaseConnectionString)) file.ConnectionString = body.DatabaseConnectionString;
        settings.SaveFile(file);
    }

    private static DbConfigViewDto DbConfigView(SettingsProvider settings)
    {
        var d = settings.Database;
        return new DbConfigViewDto(
            DatabaseProvider: EffectiveDatabaseConfig.ProviderToString(d.Provider),
            AccountId: d.AccountId,
            D1DatabaseId: d.D1DatabaseId,
            D1ApiToken: Mask(d.D1ApiToken),
            DatabaseConnectionString: Mask(d.ConnectionString),
            DatabaseConfigured: d.IsConfigured);
    }

    private static void Put(Dictionary<string, string> dict, string key, string? value, bool allowBlank)
    {
        if (value is null) return;                       // absent → leave unchanged
        if (!allowBlank && string.IsNullOrWhiteSpace(value)) return;
        dict[key] = value.Trim();
    }

    private static bool StorageOrKeyTouched(SettingsUpdateRequest b) =>
        b.StorageProvider is not null || b.StorageLocalBasePath is not null
        || b.StorageServiceUrl is not null || b.StorageRegion is not null
        || b.StorageVideoBucket is not null || b.StorageApkBucket is not null
        || b.StorageForcePathStyle is not null || b.StoragePresignTtlMinutes is not null
        || b.StorageDisablePayloadSigning is not null || b.StorageUseChecksumWhenRequired is not null
        || !string.IsNullOrWhiteSpace(b.StorageAccessKeyId)
        || !string.IsNullOrWhiteSpace(b.StorageSecretAccessKey)
        || !string.IsNullOrWhiteSpace(b.ApiKey);

    private static bool StorageConfigured(EffectiveStorageConfig st) =>
        st.IsLocal
            // Local provider: a media directory + bucket names are enough (no S3 creds).
            ? !string.IsNullOrWhiteSpace(st.LocalBasePath) && !string.IsNullOrWhiteSpace(st.VideoBucket)
            : !string.IsNullOrWhiteSpace(st.AccessKeyId)
              && !string.IsNullOrWhiteSpace(st.SecretAccessKey)
              && !string.IsNullOrWhiteSpace(st.VideoBucket);

    private static async Task<SettingsViewDto> MaskedViewAsync(
        SettingsProvider settings, AppConfigProvider appConfig, CancellationToken ct)
    {
        var dbc = settings.Database;
        var st = await appConfig.GetStorageAsync(ct);
        var apiKey = await appConfig.GetApiKeyAsync(ct);
        return new SettingsViewDto(
            // Database (on disk)
            DatabaseProvider: EffectiveDatabaseConfig.ProviderToString(dbc.Provider),
            AccountId: dbc.AccountId,
            D1DatabaseId: dbc.D1DatabaseId,
            D1ApiToken: Mask(dbc.D1ApiToken),
            DatabaseConnectionString: Mask(dbc.ConnectionString),
            DatabaseConfigured: dbc.IsConfigured,
            // Object storage (in DB)
            StorageProvider: st.IsLocal ? "local" : "s3",
            StorageLocalBasePath: st.LocalBasePath,
            StorageServiceUrl: st.ServiceUrl,
            StorageRegion: st.Region,
            StorageAccessKeyId: Mask(st.AccessKeyId),
            StorageSecretAccessKey: Mask(st.SecretAccessKey),
            StorageVideoBucket: st.VideoBucket,
            StorageApkBucket: st.ApkBucket,
            StorageForcePathStyle: st.ForcePathStyle,
            StoragePresignTtlMinutes: st.PresignTtlMinutes,
            StorageDisablePayloadSigning: st.DisablePayloadSigning,
            StorageUseChecksumWhenRequired: st.UseChecksumWhenRequired,
            StorageConfigured: StorageConfigured(st),
            // Release key (in DB)
            ApiKey: Mask(apiKey));
    }

    private static MaskedSecretDto Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return new MaskedSecretDto(IsSet: false, Last4: null);
        var last4 = secret.Length <= 4 ? secret : secret[^4..];
        return new MaskedSecretDto(IsSet: true, Last4: last4);
    }

    private static async Task<ConnectionResultDto> TestDatabaseAsync(DatabaseService db, CancellationToken ct)
    {
        if (!db.IsConfigured)
            return new ConnectionResultDto(false, "Database is not configured.");
        try
        {
            await db.SchemaInitializer.EnsureSchemaAsync(ct);
            await db.Releases.GetLatestAsync(ct);
            return new ConnectionResultDto(true, $"Database reachable ({EffectiveDatabaseConfig.ProviderToString(db.Provider)}).");
        }
        catch (Exception ex)
        {
            return new ConnectionResultDto(false, ex.Message);
        }
    }

    private static async Task<ConnectionResultDto> TestStorageAsync(
        StorageRouter storage, AppConfigProvider appConfig, CancellationToken ct)
    {
        try
        {
            var cfg = await appConfig.GetStorageAsync(ct);
            var bucket = await storage.GetVideoBucketAsync(ct);
            await storage.ProbeAsync(bucket, ct);
            return cfg.IsLocal
                ? new ConnectionResultDto(true, $"Local media directory writable ('{cfg.LocalBasePath}/{bucket}').")
                : new ConnectionResultDto(true, $"Object storage reachable (bucket '{bucket}').");
        }
        catch (Exception ex)
        {
            return new ConnectionResultDto(false, ex.Message);
        }
    }
}
