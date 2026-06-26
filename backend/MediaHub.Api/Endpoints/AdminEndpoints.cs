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
/// write path. Single-admin model: setup creates exactly one admin, after which
/// setup is permanently rejected (409).
/// </summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").WithTags("admin");

        // ---- Auth lifecycle --------------------------------------------------

        // GET /api/admin/setup-state — public.
        group.MapGet("/setup-state", async (HttpContext http, AdminRepository admins, CancellationToken ct) =>
        {
            var count = await admins.CountAsync(ct);
            var authed = http.User.Identity?.IsAuthenticated == true;
            return Results.Ok(new SetupStateDto(NeedsSetup: count == 0, Authenticated: authed));
        });

        // POST /api/admin/setup — public, but only succeeds when no admin exists.
        group.MapPost("/setup", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, CancellationToken ct) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Results.BadRequest(new { error = "username and password are required." });

            // The single-admin guard: once any admin exists, setup is closed forever.
            if (await admins.CountAsync(ct) > 0)
                return Results.Conflict(new { error = "an admin already exists; setup is closed." });

            var (hash, salt) = hasher.Hash(body.Password);
            var admin = new Admin
            {
                Id = Guid.NewGuid().ToString("n"),
                Username = username,
                PasswordHash = hash,
                PasswordSalt = salt,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            try
            {
                await admins.InsertAsync(admin, ct);
            }
            catch (D1Exception)
            {
                // Most likely a race on the UNIQUE(username) constraint — treat as conflict.
                return Results.Conflict(new { error = "an admin already exists; setup is closed." });
            }

            await SignInAsync(http, admin.Username);
            return Results.Ok(new AdminIdentityDto(admin.Username));
        });

        // POST /api/admin/login — public.
        group.MapPost("/login", async (
            CredentialsRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, CancellationToken ct) =>
        {
            var username = body.Username?.Trim();
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(body.Password))
                return Results.Json(new { error = "invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);

            var admin = await admins.GetByUsernameAsync(username, ct);
            if (admin is null || !hasher.Verify(body.Password, admin.PasswordHash, admin.PasswordSalt))
                return Results.Json(new { error = "invalid credentials." }, statusCode: StatusCodes.Status401Unauthorized);

            await SignInAsync(http, admin.Username);
            return Results.Ok(new AdminIdentityDto(admin.Username));
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
        group.MapPost("/change-password", async (
            ChangePasswordRequest body, HttpContext http,
            AdminRepository admins, PasswordHasher hasher, CancellationToken ct) =>
        {
            var username = http.User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
                return Results.Unauthorized();
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

        // ---- Releases (auth, read-only) -------------------------------------

        group.MapGet("/releases", async (AppReleaseRepository repo, CancellationToken ct) =>
        {
            var list = await repo.ListAsync(ct);
            var dto = new AdminReleaseListDto(list.Select(r => new AdminReleaseDto(
                r.VersionCode, r.VersionName, r.Notes ?? string.Empty, r.ObjectKey,
                r.SizeBytes, r.Sha256, r.MinSdk, r.PublishedAt)).ToList());
            return Results.Ok(dto);
        }).RequireAuthorization();

        // ---- Settings: Cloudflare D1 + S3-compatible storage (auth) ---------

        group.MapGet("/settings", (SettingsProvider provider) =>
            Results.Ok(MaskedView(provider.Cloudflare, provider.Storage)))
            .RequireAuthorization();

        group.MapPut("/settings", (SettingsUpdateRequest body, SettingsProvider provider) =>
        {
            // Start from the currently-persisted overrides so unspecified fields
            // are preserved, then apply the incoming edits.
            var overrides = provider.LoadOverrides();
            var cf = overrides.Cloudflare;
            var st = overrides.Storage;

            // Database (Cloudflare D1). A provided (even blank) string replaces the
            // override; null/absent leaves it unchanged.
            if (body.AccountId is not null) cf.AccountId = Blank(body.AccountId);
            if (body.D1DatabaseId is not null) cf.D1DatabaseId = Blank(body.D1DatabaseId);
            if (!string.IsNullOrWhiteSpace(body.D1ApiToken)) cf.D1ApiToken = body.D1ApiToken;

            // Object storage (S3-compatible).
            if (body.StorageServiceUrl is not null) st.ServiceUrl = Blank(body.StorageServiceUrl);
            if (body.StorageRegion is not null) st.Region = Blank(body.StorageRegion);
            if (body.StorageVideoBucket is not null) st.VideoBucket = Blank(body.StorageVideoBucket);
            if (body.StorageApkBucket is not null) st.ApkBucket = Blank(body.StorageApkBucket);
            if (body.StorageForcePathStyle is { } fps) st.ForcePathStyle = fps;
            if (body.StoragePresignTtlMinutes is { } ttl) st.PresignTtlMinutes = ttl > 0 ? ttl : null;
            if (body.StorageDisablePayloadSigning is { } dps) st.DisablePayloadSigning = dps;
            if (body.StorageUseChecksumWhenRequired is { } cwr) st.UseChecksumWhenRequired = cwr;

            // Storage secrets: blank/absent means "leave unchanged".
            if (!string.IsNullOrWhiteSpace(body.StorageAccessKeyId)) st.AccessKeyId = body.StorageAccessKeyId;
            if (!string.IsNullOrWhiteSpace(body.StorageSecretAccessKey)) st.SecretAccessKey = body.StorageSecretAccessKey;

            provider.SaveOverrides(overrides);
            return Results.Ok(MaskedView(provider.Cloudflare, provider.Storage));
        }).RequireAuthorization();

        group.MapPost("/settings/test", async (D1Client d1, S3Storage storage, CancellationToken ct) =>
        {
            var d1Result = await TestD1Async(d1, ct);
            var storageResult = await TestStorageAsync(storage, ct);
            return Results.Ok(new SettingsTestDto(d1Result, storageResult));
        }).RequireAuthorization();

        return app;
    }

    // ---- Helpers -------------------------------------------------------------

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

    private static SettingsViewDto MaskedView(EffectiveCloudflareConfig cf, EffectiveStorageConfig st) => new(
        // Database (Cloudflare D1)
        AccountId: cf.AccountId,
        D1DatabaseId: cf.D1DatabaseId,
        D1ApiToken: Mask(cf.D1ApiToken),
        // Object storage (S3-compatible)
        StorageServiceUrl: st.ServiceUrl,
        StorageRegion: st.Region,
        StorageAccessKeyId: Mask(st.AccessKeyId),
        StorageSecretAccessKey: Mask(st.SecretAccessKey),
        StorageVideoBucket: st.VideoBucket,
        StorageApkBucket: st.ApkBucket,
        StorageForcePathStyle: st.ForcePathStyle,
        StoragePresignTtlMinutes: st.PresignTtlMinutes,
        StorageDisablePayloadSigning: st.DisablePayloadSigning,
        StorageUseChecksumWhenRequired: st.UseChecksumWhenRequired);

    private static MaskedSecretDto Mask(string? secret)
    {
        if (string.IsNullOrEmpty(secret)) return new MaskedSecretDto(IsSet: false, Last4: null);
        var last4 = secret.Length <= 4 ? secret : secret[^4..];
        return new MaskedSecretDto(IsSet: true, Last4: last4);
    }

    private static async Task<ConnectionResultDto> TestD1Async(D1Client d1, CancellationToken ct)
    {
        try
        {
            await d1.QueryAsync("SELECT 1 AS ok;", ct: ct);
            return new ConnectionResultDto(true, "D1 query succeeded.");
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
