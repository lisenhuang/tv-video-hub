using MediaHub.Api.Data.D1;
using MediaHub.Api.Data.Ef;
using MediaHub.Api.Settings;

namespace MediaHub.Api.Data;

/// <summary>
/// Thrown when a catalog/release operation is attempted before the database has been
/// configured in the dashboard. Surfaced to clients as a clear 503 rather than a crash.
/// </summary>
public sealed class DatabaseNotConfiguredException()
    : Exception("The database is not configured yet. Configure it in the admin dashboard.");

/// <summary>
/// Resolves the right repository/schema implementation for the <b>currently
/// configured</b> database provider (read live from <see cref="SettingsProvider"/>),
/// so switching providers in the dashboard works with no code change. Also runs the
/// schema init lazily the first time the configured DB is used.
///
/// Registered scoped; the EF implementations it news up are cheap wrappers over the
/// singleton <see cref="EfContextFactory"/> (which caches the expensive options).
/// </summary>
public sealed class DatabaseService(
    SettingsProvider settings,
    D1Client d1,
    EfContextFactory efFactory,
    ILoggerFactory loggerFactory)
{
    private static readonly SemaphoreSlim SchemaGate = new(1, 1);
    private static volatile string? _schemaEnsuredSignature;

    public DatabaseProviderKind Provider => settings.Database.Provider;
    public bool IsConfigured => settings.Database.IsConfigured;

    public IVideoRepository Videos => Provider switch
    {
        DatabaseProviderKind.D1 => new D1VideoRepository(d1),
        DatabaseProviderKind.None => throw new DatabaseNotConfiguredException(),
        _ => new EfVideoRepository(efFactory),
    };

    public IAppReleaseRepository Releases => Provider switch
    {
        DatabaseProviderKind.D1 => new D1AppReleaseRepository(d1),
        DatabaseProviderKind.None => throw new DatabaseNotConfiguredException(),
        _ => new EfAppReleaseRepository(efFactory),
    };

    public ISchemaInitializer SchemaInitializer => Provider switch
    {
        DatabaseProviderKind.D1 => new D1SchemaInitializer(d1, loggerFactory.CreateLogger<D1SchemaInitializer>()),
        DatabaseProviderKind.None => throw new DatabaseNotConfiguredException(),
        _ => new EfSchemaInitializer(efFactory, loggerFactory.CreateLogger<EfSchemaInitializer>()),
    };

    /// <summary>
    /// Ensure the schema exists for the current provider, at most once per distinct
    /// DB configuration. No-op (returns false) if the DB isn't configured.
    /// </summary>
    public async Task<bool> TryEnsureSchemaAsync(CancellationToken ct = default)
    {
        var cfg = settings.Database;
        if (!cfg.IsConfigured) return false;

        var sig = $"{cfg.Provider}|{cfg.Signature}|{cfg.D1DatabaseId}|{cfg.AccountId}";
        if (_schemaEnsuredSignature == sig) return true;

        await SchemaGate.WaitAsync(ct);
        try
        {
            if (_schemaEnsuredSignature == sig) return true;
            await SchemaInitializer.EnsureSchemaAsync(ct);
            _schemaEnsuredSignature = sig;
            return true;
        }
        finally
        {
            SchemaGate.Release();
        }
    }
}
