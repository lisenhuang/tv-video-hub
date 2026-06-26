namespace MediaHub.Api.Options;

/// <summary>
/// OPTIONAL env/appsettings seed for <b>Cloudflare D1</b> credentials (the database).
/// Bound from the "Cloudflare" config section (env vars use the double-underscore
/// convention, e.g. <c>Cloudflare__D1__ApiToken</c>). Never commit real values.
///
/// Object storage and the release API key are NOT here — they live in the database
/// and are configured via the dashboard. Only the database connection can be seeded.
/// </summary>
public sealed class CloudflareOptions
{
    public const string SectionName = "Cloudflare";

    /// <summary>Cloudflare account id (hex string from the dashboard).</summary>
    public string AccountId { get; set; } = string.Empty;

    public D1Options D1 { get; set; } = new();

    public sealed class D1Options
    {
        /// <summary>D1 database id (uuid).</summary>
        public string DatabaseId { get; set; } = string.Empty;

        /// <summary>API token with the "D1 Edit" permission.</summary>
        public string ApiToken { get; set; } = string.Empty;

        /// <summary>Cloudflare API base; overridable for tests.</summary>
        public string ApiBaseUrl { get; set; } = "https://api.cloudflare.com/client/v4";
    }
}
