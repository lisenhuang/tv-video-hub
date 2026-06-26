namespace MediaHub.Api.Options;

/// <summary>
/// Cloudflare credentials and resource ids. Bound from the "Cloudflare" config
/// section (env vars use the double-underscore convention, e.g.
/// <c>Cloudflare__D1__ApiToken</c>). Never commit real values.
/// </summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>Cloudflare account id (hex string from the dashboard).</summary>
    public string AccountId { get; set; } = string.Empty;

    public D1Options D1 { get; set; } = new();
    public R2Options R2 { get; set; } = new();

    public sealed class D1Options
    {
        /// <summary>D1 database id (uuid).</summary>
        public string DatabaseId { get; set; } = string.Empty;

        /// <summary>API token with the "D1 Edit" permission.</summary>
        public string ApiToken { get; set; } = string.Empty;

        /// <summary>Cloudflare API base; overridable for tests.</summary>
        public string ApiBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4";
    }

    public sealed class R2Options
    {
        public string AccessKeyId { get; set; } = string.Empty;
        public string SecretAccessKey { get; set; } = string.Empty;

        /// <summary>Bucket holding video objects.</summary>
        public string VideoBucket { get; set; } = "videos";

        /// <summary>Bucket holding apk objects.</summary>
        public string ApkBucket { get; set; } = "apks";

        /// <summary>
        /// S3 endpoint. If empty it is derived as
        /// <c>https://{AccountId}.r2.cloudflarestorage.com</c>.
        /// </summary>
        public string ServiceUrl { get; set; } = string.Empty;

        /// <summary>How long presigned playback/download URLs stay valid.</summary>
        public int PresignTtlMinutes { get; set; } = 360;
    }
}
