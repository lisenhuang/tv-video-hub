namespace MediaHub.Api.Models;

/// <summary>An admin account (the <c>admins</c> table in D1). Single-admin model.</summary>
public sealed class Admin
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;

    /// <summary>Base64 PBKDF2 hash.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Base64 random salt used to derive <see cref="PasswordHash"/>.</summary>
    public string PasswordSalt { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
