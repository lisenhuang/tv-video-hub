using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Storage;

/// <summary>
/// Wraps the AWS S3 client pointed at Cloudflare R2. R2 is S3-compatible, so we
/// just override the endpoint and use SigV4 with the R2 access keys.
/// </summary>
public sealed class R2Storage
{
    private readonly IAmazonS3 _s3;
    private readonly CloudflareOptions _cf;

    public R2Storage(IOptions<CloudflareOptions> cf)
    {
        _cf = cf.Value;

        var serviceUrl = string.IsNullOrWhiteSpace(_cf.R2.ServiceUrl)
            ? $"https://{_cf.AccountId}.r2.cloudflarestorage.com"
            : _cf.R2.ServiceUrl;

        var config = new AmazonS3Config
        {
            ServiceURL = serviceUrl,
            ForcePathStyle = true,          // R2 wants path-style addressing
            AuthenticationRegion = "auto",  // R2's region token
            // R2 doesn't support the newer flexible-checksum / streaming-trailer
            // additions, so keep them off to produce clean, presignable requests.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        };

        var creds = new BasicAWSCredentials(_cf.R2.AccessKeyId, _cf.R2.SecretAccessKey);
        _s3 = new AmazonS3Client(creds, config);
    }

    public string VideoBucket => _cf.R2.VideoBucket;
    public string ApkBucket => _cf.R2.ApkBucket;
    public TimeSpan PresignTtl => TimeSpan.FromMinutes(_cf.R2.PresignTtlMinutes);

    /// <summary>Generate a short-lived presigned GET URL for streaming/download.</summary>
    public (string Url, DateTimeOffset ExpiresAt) GetPresignedGetUrl(
        string bucket, string key, TimeSpan? ttl = null, string? responseContentType = null)
    {
        var lifetime = ttl ?? PresignTtl;
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

        return (_s3.GetPreSignedURL(request), new DateTimeOffset(expires, TimeSpan.Zero));
    }

    /// <summary>Upload an object (used by the release endpoint and video uploads).</summary>
    public async Task PutAsync(
        string bucket, string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var request = new PutObjectRequest
        {
            BucketName = bucket,
            Key = key,
            InputStream = content,
            ContentType = contentType,
            AutoCloseStream = false,
            DisablePayloadSigning = true, // streaming SigV4 isn't supported by R2
        };
        await _s3.PutObjectAsync(request, ct);
    }

    public async Task<bool> ExistsAsync(string bucket, string key, CancellationToken ct = default)
    {
        try
        {
            await _s3.GetObjectMetadataAsync(bucket, key, ct);
            return true;
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }
}
