namespace MediaHub.Api.Models;

/// <summary>
/// An admin account, persisted IN THE DATABASE (the <c>admins</c> table). Single-admin
/// model. The password is stored as a PBKDF2 hash + salt (see
/// <see cref="Auth.PasswordHasher"/>).
/// </summary>
public sealed class Admin
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>Base64 PBKDF2 hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 random salt.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A single key/value setting persisted IN THE DATABASE (the <c>app_config</c> table).
/// Used for runtime config that lives in the DB rather than on disk: object-storage
/// settings and the release API key.
/// </summary>
public sealed class AppConfigEntry
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
