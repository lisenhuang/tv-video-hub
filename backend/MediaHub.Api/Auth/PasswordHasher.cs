using System.Security.Cryptography;

namespace MediaHub.Api.Auth;

/// <summary>
/// PBKDF2 (SHA-256) password hashing using only framework crypto. Produces a
/// Base64 hash + Base64 salt; verification is constant-time. No external packages.
/// </summary>
public sealed class PasswordHasher
{
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int Iterations = 100_000;
    private static readonly HashAlgorithmName Algorithm = HashAlgorithmName.SHA256;

    /// <summary>Hash a password, returning Base64 hash + Base64 salt.</summary>
    public (string Hash, string Salt) Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, HashBytes);
        return (Convert.ToBase64String(hash), Convert.ToBase64String(salt));
    }

    /// <summary>
    /// Verify a candidate password against a stored Base64 hash + Base64 salt,
    /// in constant time. Returns false if the stored values can't be decoded.
    /// </summary>
    public bool Verify(string password, string hashBase64, string saltBase64)
    {
        byte[] expected;
        byte[] salt;
        try
        {
            expected = Convert.FromBase64String(hashBase64);
            salt = Convert.FromBase64String(saltBase64);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expected.Length == 0) return false;

        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, Algorithm, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
