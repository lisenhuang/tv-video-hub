using MediaHub.Api.Models;

namespace MediaHub.Api.Data.D1;

/// <summary>Cloudflare D1 (HTTP query API) implementation of <see cref="IAppReleaseRepository"/>.</summary>
public sealed class D1AppReleaseRepository(D1Client d1) : IAppReleaseRepository
{
    public async Task<IReadOnlyList<AppRelease>> ListAsync(CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM app_releases ORDER BY version_code DESC;", ct: ct);
        return rows.Select(Map).ToList();
    }

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM app_releases ORDER BY version_code DESC LIMIT 1;", ct: ct);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<AppRelease?> GetByVersionAsync(int versionCode, CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM app_releases WHERE version_code = ? LIMIT 1;", [versionCode], ct);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    /// <summary>Insert or replace a release (re-publishing a versionCode overwrites it).</summary>
    public async Task UpsertAsync(AppRelease r, CancellationToken ct = default)
    {
        await d1.ExecuteAsync(
            """
            INSERT INTO app_releases
                (version_code, version_name, notes, object_key, size_bytes, sha256, min_sdk, published_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(version_code) DO UPDATE SET
                version_name = excluded.version_name,
                notes        = excluded.notes,
                object_key   = excluded.object_key,
                size_bytes   = excluded.size_bytes,
                sha256       = excluded.sha256,
                min_sdk      = excluded.min_sdk,
                published_at = excluded.published_at;
            """,
            [r.VersionCode, r.VersionName, r.Notes, r.ObjectKey, r.SizeBytes,
             r.Sha256, r.MinSdk, r.PublishedAt],
            ct);
    }

    private static AppRelease Map(D1Row r) => new()
    {
        VersionCode = r.GetInt("version_code") ?? 0,
        VersionName = r.GetRequiredString("version_name"),
        Notes = r.GetString("notes"),
        ObjectKey = r.GetRequiredString("object_key"),
        SizeBytes = r.GetLong("size_bytes"),
        Sha256 = r.GetString("sha256") ?? string.Empty,
        MinSdk = r.GetInt("min_sdk") ?? 23,
        PublishedAt = r.GetDate("published_at"),
    };
}
