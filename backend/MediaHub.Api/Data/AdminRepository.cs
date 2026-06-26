using MediaHub.Api.Settings;

namespace MediaHub.Api.Data;

/// <summary>
/// The single local admin account, stored in the local settings file (NOT in the
/// database). This removes the database chicken-and-egg: the first run can create an
/// admin and log in with no DB configured at all. PBKDF2 hashing stays in
/// <see cref="Auth.PasswordHasher"/>.
/// </summary>
public sealed class AdminRepository(SettingsProvider settings)
{
    /// <summary>True once an admin exists in the local store.</summary>
    public bool Exists()
    {
        var a = settings.Load().Admin;
        return a is not null
            && !string.IsNullOrWhiteSpace(a.Username)
            && !string.IsNullOrWhiteSpace(a.PasswordHash);
    }

    /// <summary>The stored admin, or null if not set up yet.</summary>
    public PersistedSettings.AdminAccount? Get() => Exists() ? settings.Load().Admin : null;

    public PersistedSettings.AdminAccount? GetByUsername(string username)
    {
        var a = Get();
        return a is not null && string.Equals(a.Username, username, StringComparison.Ordinal) ? a : null;
    }

    /// <summary>
    /// Create the single admin. Returns false if one already exists (single-admin model).
    /// </summary>
    public bool TryCreate(string username, string passwordHash, string passwordSalt)
    {
        var s = settings.Load();
        if (s.Admin is { Username: not null and not "" } && !string.IsNullOrWhiteSpace(s.Admin.PasswordHash))
            return false;

        s.Admin = new PersistedSettings.AdminAccount
        {
            Username = username,
            PasswordHash = passwordHash,
            PasswordSalt = passwordSalt,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
        };
        settings.Save(s);
        return true;
    }

    public void UpdatePassword(string passwordHash, string passwordSalt)
    {
        var s = settings.Load();
        if (s.Admin is null) return;
        s.Admin.PasswordHash = passwordHash;
        s.Admin.PasswordSalt = passwordSalt;
        settings.Save(s);
    }
}
