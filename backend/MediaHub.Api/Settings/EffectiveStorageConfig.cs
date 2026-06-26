namespace MediaHub.Api.Settings;

/// <summary>
/// The fully-resolved, provider-agnostic S3-compatible object-storage config in
/// effect at runtime: persisted dashboard overrides merged over the env/appsettings
/// defaults. Works with Cloudflare R2, AWS S3, MinIO, Backblaze B2, etc. Immutable
/// snapshot read per-operation by <see cref="Storage.S3Storage"/>.
/// </summary>
public sealed class EffectiveStorageConfig
{
    /// <summary>
    /// Active storage provider: <c>"s3"</c> (default — any S3-compatible store) or
    /// <c>"local"</c> (the server's own filesystem, served at <c>/api/media/...</c>).
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>Filesystem base directory for the local provider (e.g. <c>App_Data/media</c>).</summary>
    public required string LocalBasePath { get; init; }

    /// <summary>
    /// Server-managed HMAC key used to sign local <c>/api/media/...</c> URLs (base64).
    /// Never exposed in settings responses; auto-generated on first use if absent.
    /// </summary>
    public required string LocalSigningKey { get; init; }

    /// <summary>True when the active provider is the local filesystem.</summary>
    public bool IsLocal => string.Equals(Provider, "local", StringComparison.OrdinalIgnoreCase);

    /// <summary>S3 endpoint URL. Empty means "use the AWS regional endpoint for <see cref="Region"/>".</summary>
    public required string ServiceUrl { get; init; }

    /// <summary>Region / region token (e.g. <c>auto</c> for R2, <c>us-east-1</c> for AWS).</summary>
    public required string Region { get; init; }

    public required string AccessKeyId { get; init; }
    public required string SecretAccessKey { get; init; }
    public required string VideoBucket { get; init; }
    public required string ApkBucket { get; init; }

    public required bool ForcePathStyle { get; init; }
    public required int PresignTtlMinutes { get; init; }
    public required bool DisablePayloadSigning { get; init; }
    public required bool UseChecksumWhenRequired { get; init; }

    /// <summary>True when an explicit S3 endpoint is configured (R2/MinIO/custom).</summary>
    public bool HasServiceUrl => !string.IsNullOrWhiteSpace(ServiceUrl);

    /// <summary>
    /// A stable signature over the fields that affect the underlying
    /// <c>AmazonS3Client</c>, so a cached client is rebuilt only when one of them
    /// changes. The pipe delimiter keeps concatenated values unambiguous.
    /// </summary>
    public string ClientSignature =>
        string.Join("|",
            ServiceUrl,
            Region,
            AccessKeyId,
            SecretAccessKey,
            ForcePathStyle ? "1" : "0",
            DisablePayloadSigning ? "1" : "0",
            UseChecksumWhenRequired ? "1" : "0");
}
