namespace MediaHub.Api.Options;

/// <summary>
/// OPTIONAL env/appsettings seed for the pluggable database. Everything here is a
/// default that the local settings file (and the dashboard) can override. With no
/// value set, the database is treated as "not configured" and the dashboard setup
/// flow drives it.
///
/// Bound from the "Database" section (e.g. <c>Database__Provider</c>,
/// <c>Database__ConnectionString</c>). D1-specific fields are seeded from the
/// existing <c>Cloudflare</c> section via <see cref="CloudflareOptions"/>.
/// </summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    /// <summary>One of: <c>d1</c>, <c>sqlite</c>, <c>postgres</c>, <c>mysql</c>, <c>sqlserver</c>. Empty = not configured.</summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>Connection string for the self-hosted SQL providers.</summary>
    public string ConnectionString { get; set; } = string.Empty;
}
