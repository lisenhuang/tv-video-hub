using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Single source of truth for the config in effect. The <b>local settings file</b>
/// (via <see cref="SettingsStore"/>) is authoritative; env/appsettings options are
/// read only as OPTIONAL seeds/defaults. With no env and no file the app still
/// starts — everything is then configured through the <c>/admin</c> dashboard.
///
/// Exposes effective snapshots: <see cref="Database"/> (pluggable: D1 or SQL),
/// <see cref="Storage"/> (S3-compatible object storage), and the release
/// <see cref="ApiKey"/>. The admin account lives here too (local, no DB needed).
/// Registered as a singleton; consumers read per operation, so a dashboard edit
/// takes effect on the next request without a restart.
/// </summary>
public sealed class SettingsProvider
{
    private readonly CloudflareOptions _cloudflareSeed;
    private readonly StorageOptions _storageSeed;
    private readonly ApiOptions _apiSeed;
    private readonly DatabaseOptions _databaseSeed;
    private readonly SettingsStore _store;
    private readonly object _gate = new();

    private EffectiveDatabaseConfig _database;
    private EffectiveStorageConfig _storage;
    private string _apiKey;

    public SettingsProvider(
        IOptions<CloudflareOptions> cloudflareSeed,
        IOptions<StorageOptions> storageSeed,
        IOptions<ApiOptions> apiSeed,
        IOptions<DatabaseOptions> databaseSeed,
        SettingsStore store)
    {
        _cloudflareSeed = cloudflareSeed.Value;
        _storageSeed = storageSeed.Value;
        _apiSeed = apiSeed.Value;
        _databaseSeed = databaseSeed.Value;
        _store = store;

        var s = _store.Load();
        _database = BuildDatabase(s.Database);
        _storage = BuildStorage(s.Storage);
        _apiKey = BuildApiKey(s.Api);
    }

    /// <summary>The current effective database config snapshot.</summary>
    public EffectiveDatabaseConfig Database
    {
        get { lock (_gate) return _database; }
    }

    /// <summary>The current effective S3-compatible object-storage config snapshot.</summary>
    public EffectiveStorageConfig Storage
    {
        get { lock (_gate) return _storage; }
    }

    /// <summary>The current effective release write secret (may be empty = disabled).</summary>
    public string ApiKey
    {
        get { lock (_gate) return _apiKey; }
    }

    /// <summary>The raw persisted settings (used by the settings/admin endpoints).</summary>
    public PersistedSettings Load() => _store.Load();

    /// <summary>
    /// Persist new settings and atomically refresh all snapshots so the next
    /// request sees them.
    /// </summary>
    public void Save(PersistedSettings settings)
    {
        lock (_gate)
        {
            _store.Save(settings);
            _database = BuildDatabase(settings.Database);
            _storage = BuildStorage(settings.Storage);
            _apiKey = BuildApiKey(settings.Api);
        }
    }

    /// <summary>Re-read from disk and rebuild (e.g. if the file changed out-of-band).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            var s = _store.Load();
            _database = BuildDatabase(s.Database);
            _storage = BuildStorage(s.Storage);
            _apiKey = BuildApiKey(s.Api);
        }
    }

    private EffectiveDatabaseConfig BuildDatabase(PersistedSettings.DatabaseSettings d)
    {
        // Provider: persisted wins; else the seed default (which itself may be empty → None).
        var providerStr = !string.IsNullOrWhiteSpace(d.Provider) ? d.Provider : _databaseSeed.Provider;
        var provider = EffectiveDatabaseConfig.ParseProvider(providerStr);

        return new EffectiveDatabaseConfig
        {
            Provider = provider,
            AccountId = Pick(d.AccountId, _cloudflareSeed.AccountId),
            D1DatabaseId = Pick(d.D1DatabaseId, _cloudflareSeed.D1.DatabaseId),
            D1ApiToken = Pick(d.D1ApiToken, _cloudflareSeed.D1.ApiToken),
            D1ApiBaseUrl = string.IsNullOrWhiteSpace(_cloudflareSeed.D1.ApiBaseUrl)
                ? "https://api.cloudflare.com/client/v4"
                : _cloudflareSeed.D1.ApiBaseUrl,
            ConnectionString = Pick(d.ConnectionString, _databaseSeed.ConnectionString),
        };
    }

    private EffectiveStorageConfig BuildStorage(PersistedSettings.StorageOverrides o)
    {
        var ttl = o.PresignTtlMinutes is { } t and > 0 ? t : _storageSeed.PresignTtlMinutes;

        return new EffectiveStorageConfig
        {
            ServiceUrl = Pick(o.ServiceUrl, _storageSeed.ServiceUrl),
            Region = Pick(o.Region, _storageSeed.Region),
            AccessKeyId = Pick(o.AccessKeyId, _storageSeed.AccessKeyId),
            SecretAccessKey = Pick(o.SecretAccessKey, _storageSeed.SecretAccessKey),
            VideoBucket = Pick(o.VideoBucket, _storageSeed.VideoBucket),
            ApkBucket = Pick(o.ApkBucket, _storageSeed.ApkBucket),
            ForcePathStyle = o.ForcePathStyle ?? _storageSeed.ForcePathStyle,
            PresignTtlMinutes = ttl,
            DisablePayloadSigning = o.DisablePayloadSigning ?? _storageSeed.DisablePayloadSigning,
            UseChecksumWhenRequired = o.UseChecksumWhenRequired ?? _storageSeed.UseChecksumWhenRequired,
        };
    }

    private string BuildApiKey(PersistedSettings.ApiSettings a) => Pick(a.Key, _apiSeed.Key);

    /// <summary>Override wins when non-empty; otherwise fall back to the seed default.</summary>
    private static string Pick(string? overrideValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? defaultValue : overrideValue;
}
