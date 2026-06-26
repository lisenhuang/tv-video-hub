namespace MediaHub.Api.Models;

/// <summary>A published APK build (the <c>app_releases</c> table in D1).</summary>
public sealed class AppRelease
{
    /// <summary>Monotonic Android versionCode; also the primary key.</summary>
    public int VersionCode { get; set; }

    public string VersionName { get; set; } = string.Empty;
    public string? Notes { get; set; }

    /// <summary>Object key inside the R2 apk bucket.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
    public int MinSdk { get; set; } = 23;
    public DateTimeOffset PublishedAt { get; set; }
}
