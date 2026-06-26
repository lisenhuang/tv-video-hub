using MediaHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>EF Core implementation of <see cref="IVideoRepository"/> for self-hosted SQL.</summary>
public sealed class EfVideoRepository(EfContextFactory factory) : IVideoRepository
{
    public async Task<IReadOnlyList<Video>> ListAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Videos.AsNoTracking()
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<Video?> GetAsync(string id, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Videos.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == id, ct);
    }

    public async Task<long> DeleteAsync(string id, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Videos.Where(v => v.Id == id).ExecuteDeleteAsync(ct);
    }

    public async Task InsertAsync(Video v, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        db.Videos.Add(v);
        await db.SaveChangesAsync(ct);
    }
}
