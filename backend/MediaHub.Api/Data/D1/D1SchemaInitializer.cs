namespace MediaHub.Api.Data.D1;

/// <summary>
/// Creates the D1 schema if it isn't there yet. Uses <c>CREATE TABLE IF NOT EXISTS</c>
/// so it is safe to run repeatedly and against an existing database — additive only,
/// never destructive. Admin accounts and runtime config (storage + release key) live
/// in the DB too (<c>admins</c>, <c>app_config</c>).
/// </summary>
public sealed class D1SchemaInitializer(D1Client d1, ILogger<D1SchemaInitializer> log) : ISchemaInitializer
{
    private static readonly string[] Statements =
    [
        """
        CREATE TABLE IF NOT EXISTS videos (
            id               TEXT PRIMARY KEY,
            title            TEXT NOT NULL,
            description      TEXT,
            object_key       TEXT NOT NULL,
            thumbnail_url    TEXT,
            duration_seconds INTEGER,
            size_bytes       INTEGER,
            mime_type        TEXT NOT NULL DEFAULT 'video/mp4',
            created_at       TEXT NOT NULL
        );
        """,
        "CREATE INDEX IF NOT EXISTS ix_videos_created_at ON videos (created_at DESC);",
        """
        CREATE TABLE IF NOT EXISTS app_releases (
            version_code  INTEGER PRIMARY KEY,
            version_name  TEXT NOT NULL,
            notes         TEXT,
            object_key    TEXT NOT NULL,
            size_bytes    INTEGER NOT NULL DEFAULT 0,
            sha256        TEXT NOT NULL DEFAULT '',
            min_sdk       INTEGER NOT NULL DEFAULT 23,
            published_at  TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS admins (
            id            TEXT PRIMARY KEY,
            username      TEXT NOT NULL UNIQUE,
            password_hash TEXT NOT NULL,
            password_salt TEXT NOT NULL,
            created_at    TEXT NOT NULL
        );
        """,
        """
        CREATE TABLE IF NOT EXISTS app_config (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL DEFAULT ''
        );
        """,
    ];

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        foreach (var sql in Statements)
            await d1.ExecuteAsync(sql, ct: ct);

        // Additive, forward-only migrations for databases created before a column existed.
        // CREATE TABLE IF NOT EXISTS above never alters an existing table, so add the new
        // (nullable) column when it's missing. Idempotent and safe on production data.
        await EnsureColumnAsync("videos", "size_bytes", "INTEGER", ct);

        log.LogInformation("D1 schema ensured ({Count} statements).", Statements.Length);
    }

    /// <summary>Add <paramref name="column"/> to <paramref name="table"/> if absent (SQLite has no
    /// <c>ADD COLUMN IF NOT EXISTS</c>). Idempotent: probes for the column first, and if the ALTER
    /// still races/fails, re-checks by column rather than by error text (D1/SQLite wording varies),
    /// so re-running is a no-op. The table/column/type are trusted compile-time constants (D1 can't
    /// parameterize identifiers).</summary>
    private async Task EnsureColumnAsync(string table, string column, string type, CancellationToken ct)
    {
        if (await ColumnExistsAsync(table, column, ct)) return;

        try
        {
            await d1.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {type};", ct: ct);
        }
        catch
        {
            // A racing initializer may have added it between the check and the ALTER, or the probe
            // was momentarily unavailable. Only surface the error if the column is truly still missing.
            if (!await ColumnExistsAsync(table, column, ct)) throw;
        }
    }

    /// <summary>True if <paramref name="column"/> exists on <paramref name="table"/>. Probes with a
    /// zero-row SELECT — the D1 query API throws for an unknown column — so it doesn't depend on any
    /// error-message wording.</summary>
    private async Task<bool> ColumnExistsAsync(string table, string column, CancellationToken ct)
    {
        try
        {
            await d1.QueryAsync($"SELECT {column} FROM {table} LIMIT 0;", ct: ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
