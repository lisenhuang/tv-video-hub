using System.Net;
using MediaHub.Api.Settings;

namespace MediaHub.Api.Storage;

/// <summary>
/// Filesystem-backed <see cref="IObjectStorage"/>: stores objects under
/// <c>{LocalBasePath}/{bucket}/{key}</c> (the bucket is the configured VideoBucket /
/// ApkBucket name, reused as a subdirectory). Reads its config per operation from
/// <see cref="AppConfigProvider"/> so dashboard edits apply without a restart.
///
/// Objects are served back to clients through the public, signature-gated
/// <c>GET /api/media/{bucket}/{**key}</c> endpoint (range-capable), not via direct disk
/// links. <see cref="GetPresignedGetUrlAsync"/> mints those short-lived signed URLs.
/// </summary>
public sealed class LocalStorage(AppConfigProvider appConfig) : IObjectStorage
{
    private Task<EffectiveStorageConfig> ConfigAsync(CancellationToken ct) => appConfig.GetStorageAsync(ct);

    public async Task<string> GetVideoBucketAsync(CancellationToken ct = default) =>
        (await ConfigAsync(ct)).VideoBucket;

    public async Task<string> GetApkBucketAsync(CancellationToken ct = default) =>
        (await ConfigAsync(ct)).ApkBucket;

    public async Task<(string Url, DateTimeOffset ExpiresAt)> GetPresignedGetUrlAsync(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null,
        string? baseUrl = null, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var signingKey = await appConfig.EnsureLocalSigningKeyAsync(ct);

        var lifetime = ttl ?? TimeSpan.FromMinutes(cfg.PresignTtlMinutes);
        var expiresAt = DateTimeOffset.UtcNow.Add(lifetime);
        var exp = expiresAt.ToUnixTimeSeconds();

        var sig = LocalMediaSigner.Sign(signingKey, bucket, key, exp);

        // URL-encode each path segment but keep the slashes between them.
        var encodedKey = string.Join('/', key.Split('/').Select(WebUtility.UrlEncode));
        var prefix = string.IsNullOrWhiteSpace(baseUrl) ? string.Empty : baseUrl.TrimEnd('/');
        var url = $"{prefix}/api/media/{WebUtility.UrlEncode(bucket)}/{encodedKey}?exp={exp}&sig={sig}";

        return (url, expiresAt);
    }

    public async Task PutAsync(
        string bucket, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var fullPath = ResolveOrThrow(cfg.LocalBasePath, bucket, key);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        // Write to a temp file in the same directory then atomically move into place, so a
        // partially-written object is never observable.
        var tempPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("n");
        try
        {
            await using (var dest = File.Create(tempPath))
                await content.CopyToAsync(dest, ct);
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        return TryResolve(cfg.LocalBasePath, bucket, key, out var fullPath) && File.Exists(fullPath);
    }

    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        if (TryResolve(cfg.LocalBasePath, bucket, key, out var fullPath) && File.Exists(fullPath))
            File.Delete(fullPath);
    }

    /// <summary>
    /// "Connectivity" check for local storage: ensure the bucket directory exists and is
    /// writable by creating it and round-tripping a temp file. Throws on failure.
    /// </summary>
    public async Task ProbeAsync(string bucket, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var dir = ResolveOrThrow(cfg.LocalBasePath, bucket, ".probe");
        var dirPath = Path.GetDirectoryName(dir)!;
        Directory.CreateDirectory(dirPath);

        var probe = Path.Combine(dirPath, $".probe-{Guid.NewGuid():n}");
        await File.WriteAllTextAsync(probe, "ok", ct);
        File.Delete(probe);
    }

    /// <summary>
    /// Resolve <c>{basePath}/{bucket}/{key}</c> to an absolute path, guarding against path
    /// traversal: rejects rooted/absolute keys and any resolved path that escapes the base
    /// directory. Returns false (no throw) on a rejected/unsafe path — shared with the
    /// media endpoint so both apply the identical guard.
    /// </summary>
    public static bool TryResolve(string basePath, string bucket, string key, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(bucket) || string.IsNullOrWhiteSpace(key))
            return false;

        // Reject rooted keys/buckets and obvious traversal up-front.
        if (Path.IsPathRooted(key) || Path.IsPathRooted(bucket))
            return false;

        var baseFull = Path.GetFullPath(basePath);
        var candidate = Path.GetFullPath(Path.Combine(baseFull, bucket, key));

        // Must stay strictly under the base directory.
        var baseWithSep = baseFull.EndsWith(Path.DirectorySeparatorChar)
            ? baseFull
            : baseFull + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(baseWithSep, StringComparison.Ordinal))
            return false;

        fullPath = candidate;
        return true;
    }

    private static string ResolveOrThrow(string basePath, string bucket, string key) =>
        TryResolve(basePath, bucket, key, out var p)
            ? p
            : throw new UnauthorizedAccessException($"invalid object path '{bucket}/{key}'.");
}
