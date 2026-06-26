using MediaHub.Api.Settings;

namespace MediaHub.Api.Storage;

/// <summary>
/// <see cref="IObjectStorage"/> facade that selects the active provider PER CALL from the
/// DB-backed config (<see cref="AppConfigProvider"/>) and delegates to either
/// <see cref="S3Storage"/> or <see cref="LocalStorage"/>. Resolving per call (not at DI
/// time) lets the operator switch providers at runtime from the dashboard without a
/// restart. All endpoints inject this instead of a concrete provider.
/// </summary>
public sealed class StorageRouter(
    AppConfigProvider appConfig, S3Storage s3, LocalStorage local) : IObjectStorage
{
    private async Task<IObjectStorage> ActiveAsync(CancellationToken ct) =>
        (await appConfig.GetStorageAsync(ct)).IsLocal ? local : s3;

    public Task<string> GetVideoBucketAsync(CancellationToken ct = default) =>
        Resolve(ct, s => s.GetVideoBucketAsync(ct));

    public Task<string> GetApkBucketAsync(CancellationToken ct = default) =>
        Resolve(ct, s => s.GetApkBucketAsync(ct));

    public Task<(string Url, DateTimeOffset ExpiresAt)> GetPresignedGetUrlAsync(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null,
        string? baseUrl = null, CancellationToken ct = default) =>
        Resolve(ct, s => s.GetPresignedGetUrlAsync(bucket, key, ttl, responseContentType, baseUrl, ct));

    public Task PutAsync(
        string bucket, string key, Stream content, string contentType, CancellationToken ct = default) =>
        Resolve(ct, s => s.PutAsync(bucket, key, content, contentType, ct));

    public Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default) =>
        Resolve(ct, s => s.ExistsAsync(bucket, key, ct));

    public Task DeleteAsync(string bucket, string key, CancellationToken ct = default) =>
        Resolve(ct, s => s.DeleteAsync(bucket, key, ct));

    public Task ProbeAsync(string bucket, CancellationToken ct = default) =>
        Resolve(ct, s => s.ProbeAsync(bucket, ct));

    private async Task<T> Resolve<T>(CancellationToken ct, Func<IObjectStorage, Task<T>> op) =>
        await op(await ActiveAsync(ct));

    private async Task Resolve(CancellationToken ct, Func<IObjectStorage, Task> op) =>
        await op(await ActiveAsync(ct));
}
