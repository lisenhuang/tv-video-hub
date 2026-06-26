namespace MediaHub.Api.Settings;

/// <summary>
/// The full editable configuration persisted to the local settings file. This file
/// is the <b>single source of bootstrap truth</b>: with no env and no pre-existing
/// file, the app still starts and is configured entirely through the
/// <c>/admin</c> dashboard.
///
/// Sections:
/// <list type="bullet">
/// <item><see cref="Admin"/> — the single local admin account (no database needed to log in).</item>
/// <item><see cref="Database"/> — pluggable DB: Cloudflare D1 or a self-hosted SQL DB.</item>
/// <item><see cref="Storage"/> — provider-agnostic S3-compatible object storage.</item>
/// <item><see cref="Api"/> — the release write secret (<c>X-Api-Key</c>).</item>
/// </list>
/// Every field is nullable so an absent value means "not configured / no override".
/// </summary>
public sealed class PersistedSettings
{
    public AdminAccount? Admin { get; set; }
    public DatabaseSettings Database { get; set; } = new();
    public StorageOverrides Storage { get; set; } = new();
    public ApiSettings Api { get; set; } = new();

    /// <summary>The single local admin account. Null until the first-run wizard creates it.</summary>
    public sealed class AdminAccount
    {
        public string? Username { get; set; }

        /// <summary>Base64 PBKDF2 hash.</summary>
        public string? PasswordHash { get; set; }

        /// <summary>Base64 random salt.</summary>
        public string? PasswordSalt { get; set; }

        public string? CreatedAt { get; set; }
    }

    /// <summary>
    /// Pluggable database config. <see cref="Provider"/> selects the implementation;
    /// D1 uses the Cloudflare fields, the SQL providers use <see cref="ConnectionString"/>.
    /// </summary>
    public sealed class DatabaseSettings
    {
        /// <summary>One of: <c>d1</c>, <c>sqlite</c>, <c>postgres</c>, <c>mysql</c>, <c>sqlserver</c>. Null = not configured.</summary>
        public string? Provider { get; set; }

        // Cloudflare D1 fields (used when Provider == "d1").
        public string? AccountId { get; set; }
        public string? D1DatabaseId { get; set; }
        public string? D1ApiToken { get; set; }

        // Self-hosted SQL (used for sqlite/postgres/mysql/sqlserver).
        public string? ConnectionString { get; set; }
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

    /// <summary>API write secret for the <c>X-Api-Key</c> release endpoints.</summary>
    public sealed class ApiSettings
    {
        public string? Key { get; set; }
    }
}
