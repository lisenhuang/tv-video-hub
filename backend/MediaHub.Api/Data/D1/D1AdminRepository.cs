using MediaHub.Api.Models;

namespace MediaHub.Api.Data.D1;

/// <summary>Cloudflare D1 implementation of <see cref="IAdminRepository"/> (the <c>admins</c> table).</summary>
public sealed class D1AdminRepository(D1Client d1) : IAdminRepository
{
    public async Task<long> CountAsync(CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync("SELECT COUNT(*) AS n FROM admins;", ct: ct);
        return rows.Count == 0 ? 0 : rows[0].GetLong("n");
    }

    public async Task<Admin?> GetByUsernameAsync(string username, CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM admins WHERE username = ? LIMIT 1;", [username], ct);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task InsertAsync(Admin admin, CancellationToken ct = default)
    {
        await d1.ExecuteAsync(
            """
            INSERT INTO admins (id, username, password_hash, password_salt, created_at)
            VALUES (?, ?, ?, ?, ?);
            """,
            [admin.Id, admin.Username, admin.PasswordHash, admin.PasswordSalt, admin.CreatedAt],
            ct);
    }

    public async Task UpdatePasswordAsync(
        string username, string passwordHash, string passwordSalt, CancellationToken ct = default)
    {
        await d1.ExecuteAsync(
            "UPDATE admins SET password_hash = ?, password_salt = ? WHERE username = ?;",
            [passwordHash, passwordSalt, username],
            ct);
    }

    private static Admin Map(D1Row r) => new()
    {
        Id = r.GetRequiredString("id"),
        Username = r.GetRequiredString("username"),
        PasswordHash = r.GetRequiredString("password_hash"),
        PasswordSalt = r.GetRequiredString("password_salt"),
        CreatedAt = r.GetDate("created_at"),
    };
}
