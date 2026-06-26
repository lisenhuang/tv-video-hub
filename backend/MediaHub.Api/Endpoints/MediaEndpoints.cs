using MediaHub.Api.Settings;
using MediaHub.Api.Storage;

namespace MediaHub.Api.Endpoints;

/// <summary>
/// Public, signature-gated serving endpoint for the <b>local</b> storage provider:
/// <c>GET /api/media/{bucket}/{**key}?exp={unix}&amp;sig={hmac}</c>. Streams files off the
/// server's filesystem with HTTP range support so ExoPlayer can seek.
///
/// Access control is the signed token only (no cookie / X-Api-Key), matching the
/// "short-lived presigned URL" model the app already streams from. The route is harmless
/// when the active provider is S3 — nothing mints these URLs, and unsigned requests 403.
/// </summary>
public static class MediaEndpoints
{
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/media/{bucket}/{**key}", async (
            string bucket, string key, long? exp, string? sig,
            AppConfigProvider appConfig, CancellationToken ct) =>
        {
            if (exp is not { } expSeconds)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            var cfg = await appConfig.GetStorageAsync(ct);
            var signingKey = cfg.LocalSigningKey;

            // Validate signature + expiry (constant-time). Reject if unsigned/expired.
            if (!LocalMediaSigner.Validate(signingKey, bucket, key, expSeconds, sig))
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // Resolve under the base dir with the shared path-traversal guard.
            if (!LocalStorage.TryResolve(cfg.LocalBasePath, bucket, key, out var fullPath)
                || !File.Exists(fullPath))
                return Results.NotFound();

            var contentType = ContentTypeFor(fullPath);
            return Results.File(fullPath, contentType, enableRangeProcessing: true);
        })
        .WithTags("media")
        .ExcludeFromDescription();

        return app;
    }

    /// <summary>Infer a content type from the file extension.</summary>
    private static string ContentTypeFor(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".m4v" => "video/mp4",
            ".webm" => "video/webm",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".m3u8" => "application/x-mpegURL",
            ".mpd" => "application/dash+xml",
            ".ts" => "video/mp2t",
            ".apk" => "application/vnd.android.package-archive",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "application/octet-stream",
        };
}
