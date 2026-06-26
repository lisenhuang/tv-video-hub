using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MediaHub.Api.Settings;

namespace MediaHub.Api.Storage;

/// <summary>
/// Wraps the AWS S3 client pointed at Cloudflare R2. R2 is S3-compatible, so we
/// just override the endpoint and use SigV4 with the R2 access keys.
///
/// Credentials/endpoint are resolved per operation from
/// <see cref="CloudflareSettingsProvider"/>, so dashboard edits take effect
/// without a restart. The underlying <c>AmazonS3Client</c> is cached and only
/// rebuilt when the relevant R2 settings change (compared via a small signature).
/// </summary>
public sealed class R2Storage(CloudflareSettingsProvider settings)
{
    private readonly object _gate = new();
    private IAmazonS3? _s3;
    private string? _builtSignature;

    /// <summary>Get (or lazily rebuild) the S3 client for the current settings.</summary>
    private IAmazonS3 Client(EffectiveCloudflareConfig cf)
    {
        var signature = cf.R2Signature;
        lock (_gate)
        {
            if (_s3 is not null && _builtSignature == signature)
                return _s3;

            // Settings changed (or first use): rebuild and dispose the old client.
            _s3?.Dispose();

            var config = new AmazonS3Config
            {
                ServiceURL = cf.ResolvedR2ServiceUrl,
                ForcePathStyle = true,          // R2 wants path-style addressing
                AuthenticationRegion = "auto",  // R2's region token
                // R2 doesn't support the newer flexible-checksum / streaming-trailer
                // additions, so keep them off to produce clean, presignable requests.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            };

            var creds = new BasicAWSCredentials(cf.R2AccessKeyId, cf.R2SecretAccessKey);
            _s3 = new AmazonS3Client(creds, config);
            _builtSignature = signature;
            return _s3;
        }
    }

    public string VideoBucket => settings.Current.R2VideoBucket;
    public string ApkBucket => settings.Current.R2ApkBucket;
    public TimeSpan PresignTtl => TimeSpan.FromMinutes(settings.Current.R2PresignTtlMinutes);

    /// <summary>Generate a short-lived presigned GET URL for streaming/download.</summary>
    public (string Url, DateTimeOffset ExpiresAt) GetPresignedGetUrl(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null)
    {
        var cf = settings.Current;
        var s3 = Client(cf);

        var lifetime = ttl ?? TimeSpan.FromMinutes(cf.R2PresignTtlMinutes);
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
        var s3 = Client(settings.Current);
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true, // streaming SigV4 isn't supported by R2
        };
        await s3.PutObjectAsync(request, ct);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        var s3 = Client(settings.Current);
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

    /// <summary>Delete an object. Treated as success if the object is already gone.</summary>
    public async Task DeleteAsync(string bucket, string key, CancellationToken ct = default)
    {
        var s3 = Client(settings.Current);
        await s3.DeleteObjectAsync(bucket, key, ct);
    }

    /// <summary>
    /// Lightweight connectivity check for the settings "test connection" action:
    /// lists at most one object in the given bucket. Throws on failure.
    /// </summary>
    public async Task ProbeAsync(string bucket, CancellationToken ct = default)
    {
        var s3 = Client(settings.Current);
        await s3.ListObjectsV2Async(
            new ListObjectsV2Request { BucketName = bucket, MaxKeys = 1 }, ct);
    }
}
