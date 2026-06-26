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
/// Cookie-authenticated admin dashboard API under <c>/api/admin</c>. This is an
/// ADDITIONAL auth path; it does not touch the public endpoints or the X-Api-Key
/// write path. The admin account is stored locally (no database needed) so the very
/// first run works with zero config. Single-admin model: setup creates exactly one
/// admin, after which setup is rejected (409).
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("admin");

        // ---- Auth lifecycle --------------------------------------------------

        // GET /api/admin/setup-state — public. Drives the first-run wizard.
        group.MapGet("/setup-state", (HttpContext http, AdminRepository admins, SettingsProvider settings) =>
        {
            var needsAdmin = !admins.Exists();
            var authed = http.User.Identity?.IsAuthenticated == true;
            return Results.Ok(new SetupStateDto(
                NeedsSetup: needsAdmin,
                NeedsAdmin: needsAdmin,
                Authenticated: authed,
                NeedsDatabase: !settings.Database.IsConfigured,
                NeedsStorage: !StorageConfigured(settings.Storage)));
        });

        // POST /api/admin/setup — public, but only succeeds when no admin exists.
        group.MapPost("/setup", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Results.BadRequest(new { error = "username and password are required." });

            var (hash, salt) = hasher.Hash(body.Password);
            if (!admins.TryCreate(username, hash, salt))
                return Results.Conflict(new { error = "an admin already exists; setup is closed." });

            await SignInAsync(http, username);
            return Results.Ok(new AdminIdentityDto(username));
        });

        // POST /api/admin/login — public.
        group.MapPost("/login", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Unauthorized();

            var admin = admins.GetByUsername(username);
            if (admin is null
                || !hasher.Verify(body.Password, admin.PasswordHash ?? "", admin.PasswordSalt ?? ""))
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

        // POST /api/admin/change-password — auth. Changes the existing admin's
        // password only; never creates a new admin.
        group.MapPost("/change-password", (
            ChangePasswordRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher) =>
        {
            var username = http.User.Identity?.Name;
            if (string.IsNullOrEmpty(username)) return Results.Unauthorized();
            if (string.IsNullOrEmpty(body.CurrentPassword) || string.IsNullOrEmpty(body.NewPassword))
                return Results.BadRequest(new { error = "currentPassword and newPassword are required." });

            var admin = admins.GetByUsername(username);
            if (admin is null
                || !hasher.Verify(body.CurrentPassword, admin.PasswordHash ?? "", admin.PasswordSalt ?? ""))
                return Results.Json(new { error = "current password is incorrect." }, statusCode: StatusCodes.Status401Unauthorized);

            var (hash, salt) = hasher.Hash(body.NewPassword);
            admins.UpdatePassword(hash, salt);
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

        // ---- Releases (auth, read-only) -------------------------------------

        group.MapGet("/releases", async (AppReleaseRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct);
            var dto = new AdminReleaseListDto(list.Select(r => new AdminReleaseDto(
                r.VersionCode, r.VersionName, r.Notes ?? string.Empty, r.ObjectKey,
                r.SizeBytes, r.Sha256, r.MinSdk, r.PublishedAt)).ToList());
            return Results.Ok(dto);
        }).RequireAuthorization();

        // ---- Settings: database + storage + release key (auth) --------------

        group.MapGet("/settings", (SettingsProvider settings) =>
            Results.Ok(MaskedView(settings)))
            .RequireAuthorization();

        group.MapPut("/settings", (SettingsUpdateRequest body, SettingsProvider settings) =>
        {
            var s = settings.Load();

            // Database (pluggable).
            if (body.DatabaseProvider is not null)
            {
                var kind = EffectiveDatabaseConfig.ParseProvider(body.DatabaseProvider);
                s.Database.Provider = kind == DatabaseProviderKind.None ? null : EffectiveDatabaseConfig.ProviderToString(kind);
            }
            if (body.AccountId is not null) s.Database.AccountId = Blank(body.AccountId);
            if (body.D1DatabaseId is not null) s.Database.D1DatabaseId = Blank(body.D1DatabaseId);
            if (!string.IsNullOrWhiteSpace(body.D1ApiToken)) s.Database.D1ApiToken = body.D1ApiToken;
            if (!string.IsNullOrWhiteSpace(body.DatabaseConnectionString)) s.Database.ConnectionString = body.DatabaseConnectionString;

            // Object storage (S3-compatible).
            if (body.StorageServiceUrl is not null) s.Storage.ServiceUrl = Blank(body.StorageServiceUrl);
            if (body.StorageRegion is not null) s.Storage.Region = Blank(body.StorageRegion);
            if (body.StorageVideoBucket is not null) s.Storage.VideoBucket = Blank(body.StorageVideoBucket);
            if (body.StorageApkBucket is not null) s.Storage.ApkBucket = Blank(body.StorageApkBucket);
            if (body.StorageForcePathStyle is { } fps) s.Storage.ForcePathStyle = fps;
            if (body.StoragePresignTtlMinutes is { } ttl) s.Storage.PresignTtlMinutes = ttl > 0 ? ttl : null;
            if (body.StorageDisablePayloadSigning is { } dps) s.Storage.DisablePayloadSigning = dps;
            if (body.StorageUseChecksumWhenRequired is { } cwr) s.Storage.UseChecksumWhenRequired = cwr;
            if (!string.IsNullOrWhiteSpace(body.StorageAccessKeyId)) s.Storage.AccessKeyId = body.StorageAccessKeyId;
            if (!string.IsNullOrWhiteSpace(body.StorageSecretAccessKey)) s.Storage.SecretAccessKey = body.StorageSecretAccessKey;

            // Release write secret.
            if (!string.IsNullOrWhiteSpace(body.ApiKey)) s.Api.Key = body.ApiKey;

            settings.Save(s);
            return Results.Ok(MaskedView(settings));
        }).RequireAuthorization();

        group.MapPost("/settings/test", async (DatabaseService db, S3Storage storage, CancellationToken ct) =>
        {
            var dbResult = await TestDatabaseAsync(db, ct);
            var storageResult = await TestStorageAsync(storage, ct);
            return Results.Ok(new SettingsTestDto(dbResult, storageResult));
        }).RequireAuthorization();

        return app;
    }

    // ---- Helpers -------------------------------------------------------------

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

    private static bool StorageConfigured(EffectiveStorageConfig st) =>
        !string.IsNullOrWhiteSpace(st.AccessKeyId)
        && !string.IsNullOrWhiteSpace(st.SecretAccessKey)
        && !string.IsNullOrWhiteSpace(st.VideoBucket);

    private static SettingsViewDto MaskedView(SettingsProvider settings)
    {
        var db = settings.Database;
        var st = settings.Storage;
        return new SettingsViewDto(
            // Database
            DatabaseProvider: EffectiveDatabaseConfig.ProviderToString(db.Provider),
            AccountId: db.AccountId,
            D1DatabaseId: db.D1DatabaseId,
            D1ApiToken: Mask(db.D1ApiToken),
            DatabaseConnectionString: Mask(db.ConnectionString),
            DatabaseConfigured: db.IsConfigured,
            // Object storage
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
            // Release key
            ApiKey: Mask(settings.ApiKey));
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
            // A trivial read confirms connectivity for both D1 and EF providers.
            await db.Releases.GetLatestAsync(ct);
            return new ConnectionResultDto(true, $"Database reachable ({EffectiveDatabaseConfig.ProviderToString(db.Provider)}).");
        }
        catch (Exception ex)
        {
            return new ConnectionResultDto(false, ex.Message);
        }
    }

    private static async Task<ConnectionResultDto> TestStorageAsync(S3Storage storage, CancellationToken ct)
    {
        try
        {
            await storage.ProbeAsync(storage.VideoBucket, ct);
            return new ConnectionResultDto(true, $"Object storage reachable (bucket '{storage.VideoBucket}').");
        }
        catch (Exception ex)
        {
            return new ConnectionResultDto(false, ex.Message);
        }
    }
}
