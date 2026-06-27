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

        // Additive, forward-only migration: EnsureCreated() builds missing tables but never
        // alters an existing one, so add videos.size_bytes for databases created before it.
        // Nullable, no default → safe on top of production data; idempotent (probe first).
        await EnsureVideoSizeColumnAsync(db, ct);

        log.LogInformation("EF schema ensured for provider {Provider}.", db.Database.ProviderName);
    }

    private static async Task EnsureVideoSizeColumnAsync(MediaHubDbContext db, CancellationToken ct)
    {
        // Present already? (fresh DBs get it from EnsureCreated; migrated DBs from a prior run)
        if (await VideoSizeColumnExistsAsync(db, ct)) return;

        // SQL Server spells it "ADD <col>"; SQLite/PostgreSQL use "ADD COLUMN <col>".
        // BIGINT maps cleanly to long? on all three bundled providers.
        var sql = db.Database.IsSqlServer()
            ? "ALTER TABLE videos ADD size_bytes BIGINT"
            : "ALTER TABLE videos ADD COLUMN size_bytes BIGINT";
        try
        {
            await db.Database.ExecuteSqlRawAsync(sql, ct);
        }
        catch
        {
            // A concurrent instance may have added it between the check and the ALTER.
            // Only surface the error if the column is genuinely still missing.
            if (!await VideoSizeColumnExistsAsync(db, ct)) throw;
        }
    }

    private static async Task<bool> VideoSizeColumnExistsAsync(MediaHubDbContext db, CancellationToken ct)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("SELECT size_bytes FROM videos WHERE 1 = 0", ct);
            return true;
        }
        catch
        {
            return false;
        }
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
