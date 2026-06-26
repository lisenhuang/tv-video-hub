using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MediaHub.Api.Settings;

namespace MediaHub.Api.Storage;

/// <summary>
/// Provider-agnostic wrapper over the AWS S3 client, pointed at any S3-compatible
/// object store: Cloudflare R2, AWS S3, MinIO, Backblaze B2, etc.
///
/// Config (endpoint, region, keys, behavior toggles, buckets) now lives IN THE DATABASE
/// and is read per operation from <see cref="AppConfigProvider"/> (cached; reloaded on
/// dashboard save), so edits take effect without a restart. The underlying
/// <c>AmazonS3Client</c> is cached and rebuilt only when a relevant setting changes
/// (compared via <see cref="EffectiveStorageConfig.ClientSignature"/>).
///
/// Client construction:
/// <list type="bullet">
/// <item>If <c>ServiceUrl</c> is set → <c>ServiceURL</c> + <c>ForcePathStyle</c> +
///   <c>AuthenticationRegion = Region</c> (R2 / MinIO / custom endpoints).</item>
/// <item>If <c>ServiceUrl</c> is empty → <c>RegionEndpoint.GetBySystemName(Region)</c>
///   so the SDK targets real AWS S3 (with <c>ForcePathStyle</c> still honored).</item>
/// </list>
/// </summary>
public sealed class S3Storage(AppConfigProvider appConfig)
{
    private readonly object _gate = new();
    private IAmazonS3? _s3;
    private string? _builtSignature;

    private Task<EffectiveStorageConfig> ConfigAsync(CancellationToken ct) => appConfig.GetStorageAsync(ct);

    /// <summary>The configured video bucket name.</summary>
    public async Task<string> GetVideoBucketAsync(CancellationToken ct = default) =>
        (await ConfigAsync(ct)).VideoBucket;

    /// <summary>The configured apk bucket name.</summary>
    public async Task<string> GetApkBucketAsync(CancellationToken ct = default) =>
        (await ConfigAsync(ct)).ApkBucket;

    /// <summary>Get (or lazily rebuild) the S3 client for the current settings.</summary>
    private IAmazonS3 Client(EffectiveStorageConfig cfg)
    {
        var signature = cfg.ClientSignature;
        lock (_gate)
        {
            if (_s3 is not null && _builtSignature == signature)
                return _s3;

            // Settings changed (or first use): rebuild and dispose the old client.
            _s3?.Dispose();

            var config = new AmazonS3Config
            {
                ForcePathStyle = cfg.ForcePathStyle,
            };

            // Checksum calculation/validation: WHEN_REQUIRED keeps presigned URLs
            // clean for R2/MinIO; strict AWS setups can opt into the SDK defaults.
            if (cfg.UseChecksumWhenRequired)
            {
                config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
                config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
            }

            if (cfg.HasServiceUrl)
            {
                // Custom S3 endpoint (R2/MinIO/B2/custom).
                config.ServiceURL = cfg.ServiceUrl;
                config.AuthenticationRegion = string.IsNullOrWhiteSpace(cfg.Region) ? "auto" : cfg.Region;
            }
            else
            {
                // Real AWS S3: resolve the regional endpoint from the region name.
                var region = string.IsNullOrWhiteSpace(cfg.Region) ? "us-east-1" : cfg.Region;
                config.RegionEndpoint = RegionEndpoint.GetBySystemName(region);
            }

            var creds = new BasicAWSCredentials(cfg.AccessKeyId, cfg.SecretAccessKey);
            _s3 = new AmazonS3Client(creds, config);
            _builtSignature = signature;
            return _s3;
        }
    }

    /// <summary>Generate a short-lived presigned GET URL for streaming/download.</summary>
    public async Task<(string Url, DateTimeOffset ExpiresAt)> GetPresignedGetUrlAsync(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null,
        CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var s3 = Client(cfg);

        var lifetime = ttl ?? TimeSpan.FromMinutes(cfg.PresignTtlMinutes);
        var expires = DateTime.UtcNow.Add(lifetime);

        var request = new GetPreSignedUrlRequest
        {
            BucketName = bucket,
            Key = key,
            Verb = HttpVerb.GET,
            Expires = expires,
        };
        if (responseContentType is not null)
            request.ResponseHeaderOverrides.ContentType = responseContentType;

        return (s3.GetPreSignedURL(request), new DateTimeOffset(expires, TimeSpan.Zero));
    }

    /// <summary>Upload an object (used by the release endpoint and video uploads).</summary>
    public async Task PutAsync(
        string bucket, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var cfg = await ConfigAsync(ct);
        var s3 = Client(cfg);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            // Streaming SigV4 isn't supported by R2; toggleable for strict AWS setups.
            DisablePayloadSigning = cfg.DisablePayloadSigning,
        };
        await s3.PutObjectAsync(request, ct);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        var s3 = Client(await ConfigAsync(ct));
        try
        {
            await s3.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    /// <summary>Delete an object.</summary>
    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        var s3 = Client(await ConfigAsync(ct));
        await s3.DeleteObjectAsync(bucket, key, ct);
    }

    /// <summary>
    /// Lightweight connectivity check for the settings "test connection" action:
    /// lists at most one object in the given bucket. Throws on failure.
    /// </summary>
    public async Task ProbeAsync(string bucket, CancellationToken ct = default)
    {
        var s3 = Client(await ConfigAsync(ct));
        await s3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, ct);
    }
}
