namespace MediaHub.Api.Settings;

/// <summary>
/// The fully-resolved Cloudflare D1 configuration actually used at runtime:
/// persisted dashboard overrides merged over the env/appsettings defaults.
/// Object storage is configured separately via <see cref="EffectiveStorageConfig"/>
/// (S3-compatible, provider-agnostic). Immutable snapshot.
/// </summary>
public sealed class EffectiveCloudflareConfig
{
    public required string AccountId { get; init; }

    public required string D1DatabaseId { get; init; }
    public required string D1ApiToken { get; init; }
    public required string D1ApiBaseUrl { get; init; }
}
