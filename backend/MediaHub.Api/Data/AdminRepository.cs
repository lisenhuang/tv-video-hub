using MediaHub.Api.Models;

namespace MediaHub.Api.Data;

/// <summary>
/// Scoped facade for the admin account, now stored IN THE DATABASE (the <c>admins</c>
/// table). Ensures the schema lazily, then delegates to the provider-specific
/// implementation. Requires the database to be configured + reachable — first-run
/// flow configures the DB before creating the admin.
/// </summary>
public sealed class AdminRepository(DatabaseService db) : IAdminRepository
{
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Admins.CountAsync(ct);
    }

    public async Task<Admin?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        return await db.Admins.GetByUsernameAsync(username, ct);
    }

    public async Task InsertAsync(Admin admin, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        await db.Admins.InsertAsync(admin, ct);
    }

    public async Task UpdatePasswordAsync(
        string username, string passwordHash, string passwordSalt, CancellationToken ct = default)
    {
        await db.TryEnsureSchemaAsync(ct);
        await db.Admins.UpdatePasswordAsync(username, passwordHash, passwordSalt, ct);
    }
}
