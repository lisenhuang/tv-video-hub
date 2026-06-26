namespace MediaHub.Api.Settings;

/// <summary>Supported database providers.</summary>
public enum DatabaseProviderKind
{
    /// <summary>Not configured yet — drives the setup flow.</summary>
    None = 0,
    D1,
    Sqlite,
    Postgres,
    MySql,
    SqlServer,
}

/// <summary>
/// The fully-resolved database configuration in effect at runtime. The database is
/// pluggable: Cloudflare D1 (HTTP query API) or a self-hosted SQL database via EF
/// Core (SQLite/Postgres/MySQL/SQL Server). Immutable snapshot.
/// </summary>
public sealed class EffectiveDatabaseConfig
{
    public required DatabaseProviderKind Provider { get; init; }

    // Cloudflare D1 (Provider == D1).
    public required string AccountId { get; init; }
    public required string D1DatabaseId { get; init; }
    public required string D1ApiToken { get; init; }
    public required string D1ApiBaseUrl { get; init; }

    // Self-hosted SQL (Provider == Sqlite/Postgres/MySql/SqlServer).
    public required string ConnectionString { get; init; }

    /// <summary>True once enough is set for the selected provider to be usable.</summary>
    public bool IsConfigured => Provider switch
    {
        DatabaseProviderKind.None => false,
        DatabaseProviderKind.D1 =>
            !string.IsNullOrWhiteSpace(AccountId)
            && !string.IsNullOrWhiteSpace(D1DatabaseId)
            && !string.IsNullOrWhiteSpace(D1ApiToken),
        _ => !string.IsNullOrWhiteSpace(ConnectionString),
    };

    /// <summary>
    /// A stable signature over the fields that affect a cached EF
    /// <c>DbContextOptions</c>, so it is rebuilt only when provider/connection change.
    /// </summary>
    public string Signature => string.Join("|", Provider, ConnectionString);

    public static DatabaseProviderKind ParseProvider(string? value) =>
        (value?.Trim().ToLowerInvariant()) switch
        {
            "d1" => DatabaseProviderKind.D1,
            "sqlite" => DatabaseProviderKind.Sqlite,
            "postgres" or "postgresql" or "npgsql" => DatabaseProviderKind.Postgres,
            "mysql" or "mariadb" => DatabaseProviderKind.MySql,
            "sqlserver" or "mssql" => DatabaseProviderKind.SqlServer,
            _ => DatabaseProviderKind.None,
        };

    public static string ProviderToString(DatabaseProviderKind kind) => kind switch
    {
        DatabaseProviderKind.D1 => "d1",
        DatabaseProviderKind.Sqlite => "sqlite",
        DatabaseProviderKind.Postgres => "postgres",
        DatabaseProviderKind.MySql => "mysql",
        DatabaseProviderKind.SqlServer => "sqlserver",
        _ => "",
    };
}
