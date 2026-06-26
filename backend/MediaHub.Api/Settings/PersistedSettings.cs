namespace MediaHub.Api.Settings;

/// <summary>
/// The editable configuration as persisted to the runtime settings file (and
/// merged into the effective config). Every field is a string/nullable so an
/// absent/blank value means "no override — fall back to the env/appsettings
/// default".
///
/// Split to mirror the config split: <see cref="Cloudflare"/> holds Cloudflare D1
/// (database is Cloudflare-only), and <see cref="Storage"/> holds the
/// provider-agnostic S3-compatible object-storage overrides.
/// </summary>
public sealed class PersistedSettings
{
    public CloudflareOverrides Cloudflare { get; set; } = new();
    public StorageOverrides Storage { get; set; } = new();

    /// <summary>Cloudflare D1 overrides (database only — D1 is Cloudflare-specific).</summary>
    public sealed class CloudflareOverrides
    {
        public string? AccountId { get; set; }
        public string? D1DatabaseId { get; set; }
        public string? D1ApiToken { get; set; }
    }

    /// <summary>S3-compatible object-storage overrides (R2/AWS/MinIO/B2/…).</summary>
    public sealed class StorageOverrides
    {
        public string? ServiceUrl { get; set; }
        public string? Region { get; set; }
        public string? AccessKeyId { get; set; }
        public string? SecretAccessKey { get; set; }
        public string? VideoBucket { get; set; }
        public string? ApkBucket { get; set; }

        /// <summary>Null means "no override".</summary>
        public bool? ForcePathStyle { get; set; }

        /// <summary>Null means "no override"; 0 or negative is treated as no override.</summary>
        public int? PresignTtlMinutes { get; set; }

        public bool? DisablePayloadSigning { get; set; }
        public bool? UseChecksumWhenRequired { get; set; }
    }
}
