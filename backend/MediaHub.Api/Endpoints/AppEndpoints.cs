using MediaHub.Api.Models;
using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Endpoints;

/// <summary>
/// App self-update endpoint. The APK is NOT hosted by this backend — it lives on GitHub
/// Releases (the "latest build"). On launch the Android app calls
/// <c>GET /api/app/latest</c>; if the reported <c>versionCode</c> is newer than the
/// installed build it downloads <c>downloadUrl</c> (the GitHub asset) and verifies
/// <c>sha256</c>. All of that metadata comes from the "AppRelease" config section
/// (<see cref="AppReleaseOptions"/>) — bump it when you publish a new APK to GitHub.
/// </summary>
public static class AppEndpoints
{
    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/app").WithTags("app");

        // GET /api/app/latest — version-check metadata, or 204 when nothing is published.
        // downloadUrl points at the GitHub "latest" release asset; the app downloads it
        // directly. Shape is unchanged, so already-installed apps keep parsing it.
        group.MapGet("/latest", (IOptionsSnapshot<AppReleaseOptions> options) =>
        {
            var o = options.Value;
            if (o.VersionCode <= 0 || string.IsNullOrWhiteSpace(o.DownloadUrl))
                return Results.NoContent();

            var publishedAt = DateTimeOffset.TryParse(o.PublishedAt, out var parsed)
                ? parsed
                : DateTimeOffset.UnixEpoch;

            return Results.Ok(new AppReleaseDto(
                VersionCode: o.VersionCode,
                VersionName: string.IsNullOrWhiteSpace(o.VersionName) ? o.VersionCode.ToString() : o.VersionName,
                Notes: o.Notes ?? string.Empty,
                DownloadUrl: o.DownloadUrl,
                SizeBytes: o.SizeBytes,
                Sha256: o.Sha256 ?? string.Empty,
                MinSdk: o.MinSdk > 0 ? o.MinSdk : 23,
                PublishedAt: publishedAt));
        });

        return app;
    }
}
