namespace MediaHub.Api.Options;

/// <summary>
/// Provider-neutral S3-compatible object-storage credentials and behavior. Bound
/// from the "Storage" config section (env vars use the double-underscore
/// convention, e.g. <c>Storage__AccessKeyId</c>). Works with Cloudflare R2, AWS
/// S3, MinIO, Backblaze B2, or any S3-compatible endpoint. Never commit real values.
///
/// The defaults are tuned for R2/MinIO so an R2 setup works with no extra config;
/// strict AWS setups can flip <see cref="ForcePathStyle"/>, <see cref="DisablePayloadSigning"/>
/// and <see cref="UseChecksumWhenRequired"/> and set a real AWS <see cref="Region"/>.
/// </summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    /// <summary>
    /// S3 endpoint URL. If set, it is used directly (R2/MinIO/custom). If empty,
    /// the AWS regional endpoint for <see cref="Region"/> is used (real AWS S3).
    /// </summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>
    /// S3 region / region token. <c>auto</c> for R2, a real region such as
    /// <c>us-east-1</c> for AWS, or the configured region for MinIO/B2.
    /// </summary>
    public string Region { get; set; } = "auto";

    public string AccessKeyId { get; set; } = string.Empty;
    public string SecretAccessKey { get; set; } = string.Empty;

    /// <summary>Bucket holding video objects.</summary>
    public string VideoBucket { get; set; } = "videos";

    /// <summary>Bucket holding apk objects.</summary>
    public string ApkBucket { get; set; } = "apks";

    /// <summary>
    /// Path-style addressing (<c>endpoint/bucket/key</c>). Default <c>true</c> —
    /// required by R2/MinIO. AWS users targeting virtual-hosted style set <c>false</c>.
    /// </summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>How long presigned playback/download URLs stay valid.</summary>
    public int PresignTtlMinutes { get; set; } = 360;

    /// <summary>
    /// Disable SigV4 payload signing on uploads. Default <c>true</c> because R2
    /// doesn't support streaming SigV4; strict AWS setups may set <c>false</c>.
    /// </summary>
    public bool DisablePayloadSigning { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (default), checksum calculation/validation is set to
    /// WHEN_REQUIRED — keeps presigned URLs clean for R2. Strict AWS setups that
    /// want the SDK's default checksum behavior set this to <c>false</c>.
    /// </summary>
    public bool UseChecksumWhenRequired { get; set; } = true;
}
