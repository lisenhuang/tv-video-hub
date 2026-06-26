namespace MediaHub.Api.Settings;

/// <summary>
/// The fully-resolved Cloudflare configuration actually used at runtime:
/// persisted dashboard overrides merged over the env/appsettings defaults. All
/// fields are concrete (non-null) values ready to use. Immutable snapshot.
/// </summary>
public sealed class EffectiveCloudflareConfig
{
    public required string AccountId { get; init; }

    public required string D1DatabaseId { get; init; }
    public required string D1ApiToken { get; init; }
    public required string D1ApiBaseUrl { get; init; }

    public required string R2AccessKeyId { get; init; }
    public required string R2SecretAccessKey { get; init; }
    public required string R2VideoBucket { get; init; }
    public required string R2ApkBucket { get; init; }

    /// <summary>Empty means "derive from account id" (see <see cref="ResolvedR2ServiceUrl"/>).</summary>
    public required string R2ServiceUrl { get; init; }
    public required int R2PresignTtlMinutes { get; init; }

    /// <summary>The S3 endpoint to use, deriving the default R2 URL when none is set.</summary>
    public string ResolvedR2ServiceUrl =>
        string.IsNullOrWhiteSpace(R2ServiceUrl)
            ? $"https://{AccountId}.r2.cloudflarestorage.com"
            : R2ServiceUrl;

    /// <summary>
    /// A stable signature over the R2-relevant fields, used to decide whether a
    /// cached AmazonS3Client must be rebuilt after a settings change. The pipe
    /// delimiter keeps concatenated field values unambiguous.
    /// </summary>
    public string R2Signature =>
        string.Join("|", AccountId, R2AccessKeyId, R2SecretAccessKey, ResolvedR2ServiceUrl);
}
