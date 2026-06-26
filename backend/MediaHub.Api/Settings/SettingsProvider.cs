using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Single source of truth for the config actually in effect. It merges the
/// persisted dashboard overrides (from <see cref="SettingsStore"/>) over the
/// env/appsettings defaults: a persisted field wins only when it is non-empty,
/// otherwise the default is used.
///
/// Exposes two effective snapshots: <see cref="Cloudflare"/> (Cloudflare D1 — the
/// database is Cloudflare-specific) and <see cref="Storage"/> (provider-agnostic
/// S3-compatible object storage). Registered as a singleton.
/// <see cref="Data.D1Client"/> reads <see cref="Cloudflare"/> and
/// <see cref="Storage.S3Storage"/> reads <see cref="Storage"/> per operation, so a
/// dashboard edit takes effect on the next request without a restart.
/// </summary>
public sealed class SettingsProvider
{
    private readonly CloudflareOptions _cloudflareDefaults;
    private readonly StorageOptions _storageDefaults;
    private readonly SettingsStore _store;
    private readonly object _gate = new();

    private EffectiveCloudflareConfig _cloudflare;
    private EffectiveStorageConfig _storage;

    public SettingsProvider(
        IOptions<CloudflareOptions> cloudflareDefaults,
        IOptions<StorageOptions> storageDefaults,
        SettingsStore store)
    {
        _cloudflareDefaults = cloudflareDefaults.Value;
        _storageDefaults = storageDefaults.Value;
        _store = store;

        var overrides = _store.Load();
        _cloudflare = BuildCloudflare(overrides.Cloudflare);
        _storage = BuildStorage(overrides.Storage);
    }

    /// <summary>The current effective Cloudflare D1 config snapshot.</summary>
    public EffectiveCloudflareConfig Cloudflare
    {
        get { lock (_gate) return _cloudflare; }
    }

    /// <summary>The current effective S3-compatible object-storage config snapshot.</summary>
    public EffectiveStorageConfig Storage
    {
        get { lock (_gate) return _storage; }
    }

    /// <summary>The raw persisted overrides (used by the settings endpoints).</summary>
    public PersistedSettings LoadOverrides() => _store.Load();

    /// <summary>
    /// Persist new overrides and atomically refresh both snapshots so the next
    /// request sees them.
    /// </summary>
    public void SaveOverrides(PersistedSettings overrides)
    {
        lock (_gate)
        {
            _store.Save(overrides);
            _cloudflare = BuildCloudflare(overrides.Cloudflare);
            _storage = BuildStorage(overrides.Storage);
        }
    }

    /// <summary>Re-read from disk and rebuild (e.g. if the file changed out-of-band).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            var overrides = _store.Load();
            _cloudflare = BuildCloudflare(overrides.Cloudflare);
            _storage = BuildStorage(overrides.Storage);
        }
    }

    private EffectiveCloudflareConfig BuildCloudflare(PersistedSettings.CloudflareOverrides o) =>
        new()
        {
            AccountId = Pick(o.AccountId, _cloudflareDefaults.AccountId),
            D1DatabaseId = Pick(o.D1DatabaseId, _cloudflareDefaults.D1.DatabaseId),
            D1ApiToken = Pick(o.D1ApiToken, _cloudflareDefaults.D1.ApiToken),
            // ApiBaseUrl is not dashboard-editable; always from defaults.
            D1ApiBaseUrl = string.IsNullOrWhiteSpace(_cloudflareDefaults.D1.ApiBaseUrl)
                ? "https://api.cloudflare.com/client/v4"
                : _cloudflareDefaults.D1.ApiBaseUrl,
        };

    private EffectiveStorageConfig BuildStorage(PersistedSettings.StorageOverrides o)
    {
        var ttl = o.PresignTtlMinutes is { } t and > 0 ? t : _storageDefaults.PresignTtlMinutes;

        return new EffectiveStorageConfig
        {
            ServiceUrl = Pick(o.ServiceUrl, _storageDefaults.ServiceUrl),
            Region = Pick(o.Region, _storageDefaults.Region),
            AccessKeyId = Pick(o.AccessKeyId, _storageDefaults.AccessKeyId),
            SecretAccessKey = Pick(o.SecretAccessKey, _storageDefaults.SecretAccessKey),
            VideoBucket = Pick(o.VideoBucket, _storageDefaults.VideoBucket),
            ApkBucket = Pick(o.ApkBucket, _storageDefaults.ApkBucket),
            ForcePathStyle = o.ForcePathStyle ?? _storageDefaults.ForcePathStyle,
            PresignTtlMinutes = ttl,
            DisablePayloadSigning = o.DisablePayloadSigning ?? _storageDefaults.DisablePayloadSigning,
            UseChecksumWhenRequired = o.UseChecksumWhenRequired ?? _storageDefaults.UseChecksumWhenRequired,
        };
    }

    /// <summary>Override wins when non-empty; otherwise fall back to the default.</summary>
    private static string Pick(string? overrideValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? defaultValue : overrideValue;
}
