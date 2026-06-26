using MediaHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>EF Core implementation of <see cref="IAdminRepository"/> for self-hosted SQL.</summary>
public sealed class EfAdminRepository(EfContextFactory factory) : IAdminRepository
{
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Admins.LongCountAsync(ct);
    }

    public async Task<Admin?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        return await db.Admins.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Username == username, ct);
    }

    public async Task InsertAsync(Admin admin, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        db.Admins.Add(admin);
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdatePasswordAsync(
        string username, string passwordHash, string passwordSalt, CancellationToken ct = default)
    {
        await using var db = factory.Create();
        await db.Admins
            .Where(a => a.Username == username)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.PasswordHash, passwordHash)
                .SetProperty(a => a.PasswordSalt, passwordSalt), ct);
    }
}
