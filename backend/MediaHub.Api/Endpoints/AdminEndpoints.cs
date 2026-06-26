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

        // ---- Cloudflare settings (auth) -------------------------------------

        group.MapGet("/settings", (CloudflareSettingsProvider provider) =>
            Results.Ok(MaskedView(provider.Current)))
            .RequireAuthorization();

        group.MapPut("/settings", (SettingsUpdateRequest body, CloudflareSettingsProvider provider) =>
        {
            // Start from the currently-persisted overrides so unspecified fields
            // are preserved, then apply the incoming edits.
            var overrides = provider.LoadOverrides();

            // Non-secret fields: apply as given. A provided (even blank) value
            // replaces the override; null/absent leaves it unchanged.
            if (body.AccountId is not null) overrides.AccountId = Blank(body.AccountId);
            if (body.D1DatabaseId is not null) overrides.D1.DatabaseId = Blank(body.D1DatabaseId);
            if (body.R2VideoBucket is not null) overrides.R2.VideoBucket = Blank(body.R2VideoBucket);
            if (body.R2ApkBucket is not null) overrides.R2.ApkBucket = Blank(body.R2ApkBucket);
            if (body.R2ServiceUrl is not null) overrides.R2.ServiceUrl = Blank(body.R2ServiceUrl);
            if (body.R2PresignTtlMinutes is { } ttl) overrides.R2.PresignTtlMinutes = ttl > 0 ? ttl : null;

            // Secret fields: blank/absent means "leave unchanged"; only a non-blank
            // value updates the stored secret.
            if (!string.IsNullOrWhiteSpace(body.D1ApiToken)) overrides.D1.ApiToken = body.D1ApiToken;
            if (!string.IsNullOrWhiteSpace(body.R2AccessKeyId)) overrides.R2.AccessKeyId = body.R2AccessKeyId;
            if (!string.IsNullOrWhiteSpace(body.R2SecretAccessKey)) overrides.R2.SecretAccessKey = body.R2SecretAccessKey;

            var effective = provider.SaveOverrides(overrides);
            return Results.Ok(MaskedView(effective));
        }).RequireAuthorization();

        group.MapPost("/settings/test", async (D1Client d1, R2Storage r2, CancellationToken ct) =>
        {
            var d1Result = await TestD1Async(d1, ct);
            var r2Result = await TestR2Async(r2, ct);
            return Results.Ok(new SettingsTestDto(d1Result, r2Result));
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

    private static SettingsViewDto MaskedView(EffectiveCloudflareConfig cf) => new(
        AccountId: cf.AccountId,
        D1DatabaseId: cf.D1DatabaseId,
        D1ApiToken: Mask(cf.D1ApiToken),
        R2AccessKeyId: Mask(cf.R2AccessKeyId),
        R2SecretAccessKey: Mask(cf.R2SecretAccessKey),
        R2VideoBucket: cf.R2VideoBucket,
        R2ApkBucket: cf.R2ApkBucket,
        R2ServiceUrl: cf.R2ServiceUrl,
        R2PresignTtlMinutes: cf.R2PresignTtlMinutes);

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

    private static async Task<ConnectionResultDto> TestR2Async(R2Storage r2, CancellationToken ct)
    {
        try
        {
            await r2.ProbeAsync(r2.VideoBucket, ct);
            return new ConnectionResultDto(true, $"R2 reachable (bucket '{r2.VideoBucket}').");
        }
        catch (Exception ex)
        {
            return new ConnectionResultDto(false, ex.Message);
        }
    }
}
