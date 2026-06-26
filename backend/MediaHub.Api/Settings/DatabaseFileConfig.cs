namespace MediaHub.Api.Settings;

/// <summary>
/// The ONLY configuration stored on the server's local disk (the DB-config file,
/// default <c>App_Data/db.json</c>): how to connect to the database. Everything else
/// — admin accounts, object-storage config, and the release API key — lives IN THE
/// DATABASE.
///
/// <see cref="Provider"/> selects the implementation; D1 uses the Cloudflare fields,
/// the SQL providers use <see cref="ConnectionString"/>. Every field is nullable so an
/// absent value means "not configured".
/// </summary>
public sealed class DatabaseFileConfig
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
