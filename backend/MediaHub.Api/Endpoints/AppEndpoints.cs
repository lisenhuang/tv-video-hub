using System.Security.Cryptography;
using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Models;
using MediaHub.Api.Storage;

namespace MediaHub.Api.Endpoints;

/// <summary>
/// APK distribution + self-update endpoints. The Android app polls
/// <c>/api/app/latest</c> on launch and downloads via <c>/api/app/download</c>.
/// CI publishes builds to <c>/api/app/releases</c>.
/// </summary>
public static class AppEndpoints
{
    private const string ApkContentType = "application/vnd.android.package-archive";

    // The release APK is committed into the backend repo under wwwroot/app/ and
    // ships inside the published image, so the backend can hand out a direct
    // download link with no object storage / DB release row required. Built from
    // android-tv as a single universal APK (runs on armeabi-v7a + arm64-v8a).
    private const string BundledApkRelativePath = "app/app-release.apk";

    public static IEndpointRouteBuilder MapAppEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/app").WithTags("app");

        // GET /api/app/latest — newest release, or 204 if none yet.
        group.MapGet("/latest", async (
            HttpContext http, AppReleaseRepository repo, CancellationToken ct) =>
        {
            var latest = await repo.GetLatestAsync(ct);
            if (latest is null) return Results.NoContent();
            return Results.Ok(ToDto(latest, http));
        });

        // GET /api/app/download?versionCode=N — 302 to a presigned APK URL
        // (latest if versionCode is omitted).
        group.MapGet("/download", async (
            HttpContext http, int? versionCode, AppReleaseRepository repo, StorageRouter r2, CancellationToken ct) =>
            await RedirectToApkAsync(http, versionCode, repo, r2, ct));

        // GET /api/app/latest.apk — fixed-path alias that 302s to the latest APK.
        // Same logic as /download with no versionCode; a stable URL for the latest build.
        group.MapGet("/latest.apk", async (
            HttpContext http, AppReleaseRepository repo, StorageRouter r2, CancellationToken ct) =>
            await RedirectToApkAsync(http, null, repo, r2, ct));

        // GET /api/app/bundled.apk — directly stream the APK committed into this
        // backend's repo (wwwroot/app/), no object storage or DB release required.
        // A stable, public, direct-download link for sideloading the current build.
        // Additive + backward compatible: the self-update endpoints above are unchanged.
        group.MapGet("/bundled.apk", (IWebHostEnvironment env) =>
        {
            var path = BundledApkPath(env);
            return File.Exists(path)
                ? Results.File(path, ApkContentType,
                    fileDownloadName: "tv-video-hub.apk", enableRangeProcessing: true)
                : Results.NotFound(new { error = "no bundled APK ships with this backend build." });
        });

        // POST /api/app/releases — CI publishes a build (multipart). Requires X-Api-Key.
        group.MapPost("/releases", PublishReleaseAsync)
            .AddEndpointFilter<ApiKeyFilter>()
            .DisableAntiforgery();

        return app;
    }

    /// <summary>Absolute path to the committed bundled APK under the web root.</summary>
    private static string BundledApkPath(IWebHostEnvironment env) =>
        Path.Combine(
            env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
            Path.Combine(BundledApkRelativePath.Split('/')));

    private static async Task<IResult> RedirectToApkAsync(
        HttpContext http, int? versionCode, AppReleaseRepository repo, StorageRouter r2, CancellationToken ct)
    {
        var release = versionCode is { } vc
            ? await repo.GetByVersionAsync(vc, ct)
            : await repo.GetLatestAsync(ct);
        if (release is null) return Results.NotFound();

        var apkBucket = await r2.GetApkBucketAsync(ct);
        // Pass this backend's base URL so local (/api/media/...) download URLs are absolute.
        var (url, _) = await r2.GetPresignedGetUrlAsync(
            apkBucket, release.ObjectKey, responseContentType: ApkContentType,
            baseUrl: $"{http.Request.Scheme}://{http.Request.Host}", ct: ct);
        return Results.Redirect(url);
    }

    private static async Task<IResult> PublishReleaseAsync(
        HttpContext http, AppReleaseRepository repo, StorageRouter r2, CancellationToken ct)
    {
        if (!http.Request.HasFormContentType)
            return Results.BadRequest(new { error = "expected multipart/form-data." });

        var form = await http.Request.ReadFormAsync(ct);
        var apk = form.Files["apk"];
        if (apk is null || apk.Length == 0)
            return Results.BadRequest(new { error = "an 'apk' file part is required." });
        if (!int.TryParse(form["versionCode"], out var versionCode) || versionCode <= 0)
            return Results.BadRequest(new { error = "a positive integer 'versionCode' is required." });

        var versionName = form["versionName"].ToString();
        if (string.IsNullOrWhiteSpace(versionName)) versionName = versionCode.ToString();
        var notes = form["notes"].ToString();
        var minSdk = int.TryParse(form["minSdk"], out var ms) ? ms : 23;

        var key = $"apks/{versionCode}/app-{versionName}.apk";

        // Hash and upload in a single pass over a buffered temp copy so we don't
        // hold the whole apk in memory.
        var tempPath = Path.GetTempFileName();
        string sha256Hex;
        try
        {
            await using (var temp = File.Create(tempPath))
            await using (var src = apk.OpenReadStream())
            {
                await src.CopyToAsync(temp, ct);
            }

            await using (var hashStream = File.OpenRead(tempPath))
            {
                var hash = await SHA256.HashDataAsync(hashStream, ct);
                sha256Hex = Convert.ToHexStringLower(hash);
            }

            var apkBucket = await r2.GetApkBucketAsync(ct);
            await using (var upload = File.OpenRead(tempPath))
            {
                await r2.PutAsync(apkBucket, key, upload, ApkContentType, ct);
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }

        var release = new AppRelease
        {
            VersionCode = versionCode,
            VersionName = versionName,
            Notes = notes,
            ObjectKey = key,
            SizeBytes = apk.Length,
            Sha256 = sha256Hex,
            MinSdk = minSdk,
            PublishedAt = DateTimeOffset.UtcNow,
        };
        await repo.UpsertAsync(release, ct);

        return Results.Created($"/api/app/download?versionCode={versionCode}", ToDto(release, http));
    }

    private static AppReleaseDto ToDto(AppRelease r, HttpContext http)
    {
        var baseUrl = $"{http.Request.Scheme}://{http.Request.Host}";
        return new AppReleaseDto(
            r.VersionCode,
            r.VersionName,
            r.Notes ?? string.Empty,
            $"{baseUrl}/api/app/download?versionCode={r.VersionCode}",
            r.SizeBytes,
            r.Sha256,
            r.MinSdk,
            r.PublishedAt);
    }
}
