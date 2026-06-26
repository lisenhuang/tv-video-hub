using MediaHub.Api.Settings;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>
/// Builds <see cref="MediaHubDbContext"/> instances for the currently-configured SQL
/// provider. <see cref="DbContextOptions"/> are expensive to build, so they are
/// cached and rebuilt only when the provider/connection signature changes (mirrors
/// the S3 client caching). A fresh context is created per call (per scope).
///
/// Singleton — the cache is shared; contexts themselves are not.
/// </summary>
public sealed class EfContextFactory(SettingsProvider settings)
{
    private readonly object _gate = new();
    private string? _signature;
    private DbContextOptions<MediaHubDbContext>? _options;

    /// <summary>Create a context for the current DB settings, or throw if not usable.</summary>
    public MediaHubDbContext Create()
    {
        var cfg = settings.Database;
        return new MediaHubDbContext(OptionsFor(cfg));
    }

    private DbContextOptions<MediaHubDbContext> OptionsFor(EffectiveDatabaseConfig cfg)
    {
        var signature = cfg.Signature;
        lock (_gate)
        {
            if (_options is not null && _signature == signature)
                return _options;

            _options = Build(cfg);
            _signature = signature;
            return _options;
        }
    }

    private static DbContextOptions<MediaHubDbContext> Build(EffectiveDatabaseConfig cfg)
    {
        if (string.IsNullOrWhiteSpace(cfg.ConnectionString) && cfg.Provider is not DatabaseProviderKind.Sqlite)
            throw new InvalidOperationException("Database connection string is not configured.");

        var builder = new DbContextOptionsBuilder<MediaHubDbContext>();
        switch (cfg.Provider)
        {
            case DatabaseProviderKind.Sqlite:
                builder.UseSqlite(string.IsNullOrWhiteSpace(cfg.ConnectionString)
                    ? "Data Source=App_Data/mediahub.db"
                    : cfg.ConnectionString);
                break;

            case DatabaseProviderKind.Postgres:
                builder.UseNpgsql(cfg.ConnectionString);
                break;

            case DatabaseProviderKind.SqlServer:
                builder.UseSqlServer(cfg.ConnectionString);
                break;

            case DatabaseProviderKind.MySql:
                // No Pomelo.EntityFrameworkCore.MySql build currently targets EF Core 10,
                // so the MySQL provider package is intentionally not bundled. The provider
                // is recognized and selectable; enable it by adding a compatible Pomelo
                // package and calling builder.UseMySql(...) here.
                throw new NotSupportedException(
                    "MySQL is selected but its EF Core 10 provider is not bundled. " +
                    "Add a Pomelo.EntityFrameworkCore.MySql build compatible with EF Core 10 " +
                    "and wire builder.UseMySql(...) in EfContextFactory.");

            default:
                throw new InvalidOperationException(
                    $"Provider '{cfg.Provider}' is not an EF Core SQL provider.");
        }

        return builder.Options;
    }
}
