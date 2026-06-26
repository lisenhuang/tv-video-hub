using System.Security.Cryptography;
using System.Text;

namespace MediaHub.Api.Storage;

/// <summary>
/// Shared HMAC signing for local <c>/api/media/...</c> URLs. The same routine is used by
/// <see cref="LocalStorage"/> (to mint signed URLs) and the media endpoint (to validate
/// them), so they can never drift.
///
/// Signature: <c>base64url(HMACSHA256(key = LocalSigningKey, data = "{bucket}/{key}|{exp}"))</c>
/// where <c>exp</c> is unix seconds. The <c>key</c> path is the raw (un-encoded) object
/// key, joined to the bucket with a single slash.
/// </summary>
public static class LocalMediaSigner
{
    /// <summary>Compute the signature for a (bucket, key, exp) tuple.</summary>
    public static string Sign(string signingKey, string bucket, string key, long exp)
    {
        var data = $"{bucket}/{key}|{exp}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(signingKey),
            Encoding.UTF8.GetBytes(data));
        return Base64UrlEncode(hash);
    }

    /// <summary>Constant-time validation of a presented signature + expiry.</summary>
    public static bool Validate(string signingKey, string bucket, string key, long exp, string? presentedSig)
    {
        if (string.IsNullOrEmpty(signingKey) || string.IsNullOrEmpty(presentedSig))
            return false;
        if (exp < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            return false;

        var expected = Sign(signingKey, bucket, key, exp);
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(presentedSig);
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
