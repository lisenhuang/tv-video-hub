namespace MediaHub.Api.Storage;

/// <summary>
/// Provider-agnostic object-storage surface used by the endpoints. Two implementations
/// exist: <see cref="S3Storage"/> (any S3-compatible store) and <see cref="LocalStorage"/>
/// (the server's own filesystem). <see cref="StorageRouter"/> picks the active one per
/// call from the DB-backed config, so the operator can switch providers at runtime.
///
/// The bucket arguments are the same logical bucket names the catalog already uses
/// (the configured VideoBucket / ApkBucket); the local provider reuses them as
/// subdirectory names.
/// </summary>
public interface IObjectStorage
{
    /// <summary>The configured video bucket name.</summary>
    Task<string> GetVideoBucketAsync(CancellationToken ct = default);

    /// <summary>The configured apk bucket name.</summary>
    Task<string> GetApkBucketAsync(CancellationToken ct = default);

    /// <summary>
    /// Produce a short-lived GET URL for streaming/download. For S3 this is a presigned
    /// URL (and <paramref name="baseUrl"/> is ignored). For local storage it is an
    /// absolute <c>{baseUrl}/api/media/...</c> URL signed with an HMAC token; callers
    /// pass <paramref name="baseUrl"/> = <c>"{scheme}://{host}"</c> so the URL points at
    /// this backend.
    /// </summary>
    Task<(string Url, DateTimeOffset ExpiresAt)> GetPresignedGetUrlAsync(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null,
        string? baseUrl = null, CancellationToken ct = default);

    /// <summary>Upload an object (used by the release endpoint and video uploads).</summary>
    Task PutAsync(string bucket, string key, Stream content, string contentType, CancellationToken ct = default);

    /// <summary>Whether an object exists.</summary>
    Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Delete an object (best-effort; missing is not an error for callers).</summary>
    Task DeleteAsync(string bucket, string key, CancellationToken ct = default);

    /// <summary>Lightweight connectivity/writability check for the settings "test" action.</summary>
    Task ProbeAsync(string bucket, CancellationToken ct = default);
}
