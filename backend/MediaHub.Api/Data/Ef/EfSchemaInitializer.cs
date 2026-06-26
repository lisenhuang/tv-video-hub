using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>
/// Ensures the EF Core schema exists via <c>EnsureCreated()</c> (additive; creates
/// the tables if the database has none). For SQLite, also ensures the data directory
/// exists. Never destructive.
/// </summary>
public sealed class EfSchemaInitializer(EfContextFactory factory, ILogger<EfSchemaInitializer> log) : ISchemaInitializer
{
    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();

        // For a file-based SQLite DB, make sure the directory exists first.
        var conn = db.Database.GetConnectionString();
        EnsureSqliteDirectory(conn);

        await db.Database.EnsureCreatedAsync(ct);
        log.LogInformation("EF schema ensured for provider {Provider}.", db.Database.ProviderName);
    }

    private static void EnsureSqliteDirectory(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        foreach (var part in connectionString.Split(';'))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 2 && kv[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
            {
                var path = kv[1].Trim();
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            }
        }
    }
}
