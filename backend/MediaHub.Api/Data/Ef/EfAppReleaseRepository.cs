using MediaHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>EF Core implementation of <see cref="IAppReleaseRepository"/> for self-hosted SQL.</summary>
public sealed class EfAppReleaseRepository(EfContextFactory factory) : IAppReleaseRepository
{
    public async Task<IReadOnlyList<AppRelease>> ListAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.AppReleases.AsNoTracking()
            .OrderByDescending(r => r.VersionCode)
            .ToListAsync(ct);
    }

    public async Task<AppRelease?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.AppReleases.AsNoTracking()
            .OrderByDescending(r => r.VersionCode)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AppRelease?> GetByVersionAsync(int versionCode, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.AppReleases.AsNoTracking()
            .FirstOrDefaultAsync(r => r.VersionCode == versionCode, ct);
    }

    /// <summary>Insert or replace a release (re-publishing a versionCode overwrites it).</summary>
    public async Task UpsertAsync(AppRelease r, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var existing = await db.AppReleases.FirstOrDefaultAsync(x => x.VersionCode == r.VersionCode, ct);
        if (existing is null)
        {
            db.AppReleases.Add(r);
        }
        else
        {
            existing.VersionName = r.VersionName;
            existing.Notes = r.Notes;
            existing.ObjectKey = r.ObjectKey;
            existing.SizeBytes = r.SizeBytes;
            existing.Sha256 = r.Sha256;
            existing.MinSdk = r.MinSdk;
            existing.PublishedAt = r.PublishedAt;
        }
        await db.SaveChangesAsync(ct);
    }
}
