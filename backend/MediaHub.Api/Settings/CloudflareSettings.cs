namespace MediaHub.Api.Settings;

/// <summary>
/// The editable Cloudflare configuration as persisted to the runtime settings
/// file (and as merged into the effective config). Every field is a string or
/// nullable so an absent/blank value means "no override — fall back to the
/// env/appsettings default". Mirrors <see cref="Options.CloudflareOptions"/>'s
/// editable surface (it intentionally omits <c>D1.ApiBaseUrl</c>, which is not
/// dashboard-editable).
/// </summary>
public sealed class CloudflareSettings
{
    public string? AccountId { get; set; }

    public D1Settings D1 { get; set; } = new();
    public R2Settings R2 { get; set; } = new();

    public sealed class D1Settings
    {
        public string? DatabaseId { get; set; }
        public string? ApiToken { get; set; }
    }

    public sealed class R2Settings
    {
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
        public string? VideoBucket { get; set; }
        public string? ApkBucket { get; set; }
        public string? ServiceUrl { get; set; }

        /// <summary>Null means "no override"; 0 or negative is treated as no override.</summary>
        public int? PresignTtlMinutes { get; set; }
    }
}
