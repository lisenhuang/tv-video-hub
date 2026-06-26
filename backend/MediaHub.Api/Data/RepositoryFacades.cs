using MediaHub.Api.Models;

namespace MediaHub.Api.Data;

/// <summary>
/// Scoped facade injected into endpoints/services. It ensures the schema exists for
/// the currently-configured provider (lazily, once per DB config) and then delegates
/// to the resolved provider implementation. This is what lets a dashboard provider
/// switch take effect with no restart and no code change at the call sites.
/// </summary>
public sealed class VideoRepository(DatabaseService db) : IVideoRepository
{
    public async Task<IReadOnlyList<Video>> ListAsync(CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Videos.ListAsync(ct);
    }

    public async Task<Video?> GetAsync(string id, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Videos.GetAsync(id, ct);
    }

    public async Task<long> DeleteAsync(string id, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Videos.DeleteAsync(id, ct);
    }

    public async Task InsertAsync(Video v, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        await db.Videos.InsertAsync(v, ct);
    }
}

/// <summary>Scoped facade for releases (see <see cref="VideoRepository"/>).</summary>
public sealed class AppReleaseRepository(DatabaseService db) : IAppReleaseRepository
{
    public async Task<IReadOnlyList<AppRelease>> ListAsync(CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Releases.ListAsync(ct);
    }

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Releases.GetLatestAsync(ct);
    }

    public async Task<AppRelease?> GetByVersionAsync(int versionCode, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Releases.GetByVersionAsync(versionCode, ct);
    }

    public async Task UpsertAsync(AppRelease r, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        await db.Releases.UpsertAsync(r, ct);
    }
}
