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

        log.LogInformation("D1 schema ensured ({Count} statements).", Statements.Length);
    }
}
