using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Single source of truth for the Cloudflare config actually in effect. It merges
/// the persisted dashboard overrides (from <see cref="SettingsStore"/>) over the
/// env/appsettings defaults (<see cref="CloudflareOptions"/>): a persisted field
/// wins only when it is non-empty, otherwise the default is used.
///
/// Registered as a singleton. <see cref="Data.D1Client"/> and
/// <see cref="Storage.R2Storage"/> read <see cref="Current"/> per operation, so a
/// dashboard edit takes effect on the next request without a restart.
/// </summary>
public sealed class CloudflareSettingsProvider
{
    private readonly CloudflareOptions _defaults;
    private readonly SettingsStore _store;
    private readonly object _gate = new();

    private EffectiveCloudflareConfig _current;

    public CloudflareSettingsProvider(IOptions<CloudflareOptions> defaults, SettingsStore store)
    {
        _defaults = defaults.Value;
        _store = store;
        _current = Build(_store.Load());
    }

    /// <summary>The current effective config snapshot.</summary>
    public EffectiveCloudflareConfig Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>The raw persisted overrides (used by the settings endpoints).</summary>
    public CloudflareSettings LoadOverrides() => _store.Load();

    /// <summary>
    /// Persist new overrides and atomically refresh <see cref="Current"/> so the
    /// next request sees them. Returns the freshly-built effective config.
    /// </summary>
    public EffectiveCloudflareConfig SaveOverrides(CloudflareSettings overrides)
    {
        lock (_gate)
        {
            _store.Save(overrides);
            _current = Build(overrides);
            return _current;
        }
    }

    /// <summary>Re-read from disk and rebuild (e.g. if the file changed out-of-band).</summary>
    public EffectiveCloudflareConfig Refresh()
    {
        lock (_gate)
        {
            _current = Build(_store.Load());
            return _current;
        }
    }

    private EffectiveCloudflareConfig Build(CloudflareSettings o)
    {
        var ttl = o.R2.PresignTtlMinutes is { } t and > 0 ? t : _defaults.R2.PresignTtlMinutes;

        return new EffectiveCloudflareConfig
        {
            AccountId = Pick(o.AccountId, _defaults.AccountId),
            D1DatabaseId = Pick(o.D1.DatabaseId, _defaults.D1.DatabaseId),
            D1ApiToken = Pick(o.D1.ApiToken, _defaults.D1.ApiToken),
            // ApiBaseUrl is not dashboard-editable; always from defaults.
            D1ApiBaseUrl = string.IsNullOrWhiteSpace(_defaults.D1.ApiBaseUrl)
                ? "https://api.cloudflare.com/client/v4"
                : _defaults.D1.ApiBaseUrl,
            R2AccessKeyId = Pick(o.R2.AccessKeyId, _defaults.R2.AccessKeyId),
            R2SecretAccessKey = Pick(o.R2.SecretAccessKey, _defaults.R2.SecretAccessKey),
            R2VideoBucket = Pick(o.R2.VideoBucket, _defaults.R2.VideoBucket),
            R2ApkBucket = Pick(o.R2.ApkBucket, _defaults.R2.ApkBucket),
            R2ServiceUrl = Pick(o.R2.ServiceUrl, _defaults.R2.ServiceUrl),
            R2PresignTtlMinutes = ttl,
        };
    }

    /// <summary>Override wins when non-empty; otherwise fall back to the default.</summary>
    private static string Pick(string? overrideValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? defaultValue : overrideValue;
}
