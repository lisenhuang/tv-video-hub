using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Source of truth for the <b>database connection</b> only. The on-disk DB-config file
/// (via <see cref="DbConfigStore"/>) is authoritative; env/appsettings are read only as
/// OPTIONAL seeds. With no env and no file the app still starts — the dashboard
/// configures the DB first, and everything else (admin, storage, api key) then lives
/// IN THE DATABASE (see <see cref="AppConfigProvider"/>).
///
/// Registered as a singleton. <see cref="Data.D1Client"/>, <see cref="Data.Ef.EfContextFactory"/>
/// and <see cref="Data.DatabaseService"/> read <see cref="Database"/> per operation, so a
/// dashboard DB-config save takes effect without a restart.
/// </summary>
public sealed class SettingsProvider
{
    private readonly CloudflareOptions _cloudflareSeed;
    private readonly DatabaseOptions _databaseSeed;
    private readonly DbConfigStore _store;
    private readonly object _gate = new();

    private EffectiveDatabaseConfig _database;

    public SettingsProvider(
        IOptions<CloudflareOptions> cloudflareSeed,
        IOptions<DatabaseOptions> databaseSeed,
        DbConfigStore store)
    {
        _cloudflareSeed = cloudflareSeed.Value;
        _databaseSeed = databaseSeed.Value;
        _store = store;

        _database = BuildDatabase(_store.Load());
    }

    /// <summary>The current effective database config snapshot.</summary>
    public EffectiveDatabaseConfig Database
    {
        get { lock (_gate) return _database; }
    }

    /// <summary>The raw on-disk DB config (used by the settings endpoints).</summary>
    public DatabaseFileConfig LoadFile() => _store.Load();

    /// <summary>Persist new DB config and atomically refresh the snapshot.</summary>
    public void SaveFile(DatabaseFileConfig config)
    {
        lock (_gate)
        {
            _store.Save(config);
            _database = BuildDatabase(config);
        }
    }

    /// <summary>Re-read from disk and rebuild (e.g. if the file changed out-of-band).</summary>
    public void Refresh()
    {
        lock (_gate)
        {
            _database = BuildDatabase(_store.Load());
        }
    }

    private EffectiveDatabaseConfig BuildDatabase(DatabaseFileConfig d)
    {
        // Provider: file wins; else the seed default (which itself may be empty → None).
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

    /// <summary>Override wins when non-empty; otherwise fall back to the seed default.</summary>
    private static string Pick(string? overrideValue, string defaultValue) =>
        string.IsNullOrWhiteSpace(overrideValue) ? defaultValue : overrideValue;
}
