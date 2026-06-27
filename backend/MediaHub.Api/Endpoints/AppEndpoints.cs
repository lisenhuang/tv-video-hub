using MediaHub.Api.Auth;
using MediaHub.Api.Models;
using MediaHub.Api.Options;
using MediaHub.Api.Settings;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Endpoints;

/// <summary>
/// App self-update endpoint. The APK is committed INTO this backend (a single static file
/// at <c>wwwroot/app/app-release.apk</c>) and served directly by the static-file middleware.
/// On launch the Android app calls <c>GET /api/app/latest</c>; if the reported
/// <c>versionCode</c> is newer than the installed build it downloads <c>downloadUrl</c> and
/// verifies <c>sha256</c>. The metadata comes from the "AppRelease" config section
/// (<see cref="AppReleaseOptions"/>); <c>downloadUrl</c> is built from the request's own base
/// URL + <see cref="AppReleaseOptions.DownloadPath"/>, so it points back at this same backend.
/// </summary>
public static class AppEndpoints
{
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/app").WithTags("app");

        // GET /api/app/latest — version-check metadata, or 204 when nothing is published.
        // downloadUrl is an ABSOLUTE url to the APK served by this backend (composed from the
        // caller's own base url). The response shape is unchanged, so already-installed apps
        // keep parsing it — and they still get an absolute url they can hand to DownloadManager.
        group.MapGet("/latest", (HttpContext http, IOptionsSnapshot<AppReleaseOptions> options) =>
        {
            var o = options.Value;
            var downloadUrl = ResolveDownloadUrl(http.Request, o);
            if (o.VersionCode <= 0 || string.IsNullOrWhiteSpace(downloadUrl))
                return Results.NoContent();

            var publishedAt = DateTimeOffset.TryParse(o.PublishedAt, out var parsed)
                ? parsed
                : DateTimeOffset.UnixEpoch;

            return Results.Ok(new AppReleaseDto(
                VersionCode: o.VersionCode,
                VersionName: string.IsNullOrWhiteSpace(o.VersionName) ? o.VersionCode.ToString() : o.VersionName,
                Notes: o.Notes ?? string.Empty,
                DownloadUrl: downloadUrl,
                SizeBytes: o.SizeBytes,
                Sha256: o.Sha256 ?? string.Empty,
                MinSdk: o.MinSdk > 0 ? o.MinSdk : 23,
                PublishedAt: publishedAt,
                ForceUpdate: o.ForceUpdate));
        });

        // GET /api/app/access — does the app need an access code, and is the one it sent valid?
        // Ungated (the app calls this BEFORE it has access) and additive, so older apps that never
        // call it are unaffected. The app sends its stored code in the X-Access-Code header.
        group.MapGet("/access", async (HttpContext http, AppConfigProvider appConfig) =>
        {
            var ct = http.RequestAborted;
            var required = await appConfig.GetAccessGateEnabledAsync(ct);
            var provided = http.Request.Headers[AccessCodeFilter.HeaderName].ToString();
            var valid = !required || await appConfig.IsAccessCodeValidAsync(provided, ct);
            return Results.Ok(new AccessStatusDto(required, valid));
        });

        return app;
    }

    /// <summary>
    /// Resolve the absolute APK download URL. An explicit absolute <see cref="AppReleaseOptions.DownloadUrl"/>
    /// wins (host the APK elsewhere, or pin a public URL behind a rewriting proxy). Otherwise prefix
    /// <see cref="AppReleaseOptions.DownloadPath"/> with the request's own base (honoring
    /// X-Forwarded-Proto/Host) so the app downloads from the SAME backend it queried.
    /// </summary>
    private static string ResolveDownloadUrl(HttpRequest request, AppReleaseOptions o)
    {
        if (!string.IsNullOrWhiteSpace(o.DownloadUrl))
            return o.DownloadUrl.Trim();
        if (string.IsNullOrWhiteSpace(o.DownloadPath))
            return string.Empty;

        var scheme = ForwardedFirst(request, "X-Forwarded-Proto") ?? request.Scheme;
        var host = ForwardedFirst(request, "X-Forwarded-Host") ?? request.Host.Value;
        if (string.IsNullOrWhiteSpace(host))
            return string.Empty;

        var path = o.DownloadPath.StartsWith('/') ? o.DownloadPath : "/" + o.DownloadPath;
        var pathBase = request.PathBase.Value ?? string.Empty;
        return $"{scheme}://{host}{pathBase}{path}";
    }

    /// <summary>First value of a possibly comma-listed forwarded header, or null when absent.</summary>
    private static string? ForwardedFirst(HttpRequest request, string header)
    {
        var raw = request.Headers[header].ToString();
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var first = raw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(first) ? null : first;
    }
}
