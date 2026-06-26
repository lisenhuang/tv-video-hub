namespace MediaHub.Api.Options;

/// <summary>
/// Describes the APK that the Android app self-updates to. The APK itself is NOT hosted
/// by this backend — it lives on GitHub Releases (the "latest build"). This is just the
/// metadata the version-check endpoint (<c>GET /api/app/latest</c>) returns: the app
/// compares <see cref="VersionCode"/> to its own build and, if newer, downloads
/// <see cref="DownloadUrl"/> and verifies <see cref="Sha256"/>.
///
/// Bound from the "AppRelease" config section (appsettings / env). Bump
/// <see cref="VersionCode"/> + <see cref="Sha256"/> here whenever you publish a new APK
/// to GitHub Releases. While <see cref="VersionCode"/> equals the installed build the app
/// shows no update (and never checks the hash), so a blank <see cref="Sha256"/> is fine
/// until the first real version bump.
/// </summary>
public sealed class AppReleaseOptions
{
    public const string SectionName = "AppRelease";

    /// <summary>Monotonic build number; the app updates only if this is &gt; its own VERSION_CODE.</summary>
    public int VersionCode { get; set; }

    /// <summary>Human-readable version (e.g. "1.0.0"). Defaults to the version code if blank.</summary>
    public string VersionName { get; set; } = string.Empty;

    /// <summary>Minimum Android SDK the APK supports.</summary>
    public int MinSdk { get; set; } = 23;

    /// <summary>Optional changelog shown in the "Update available" prompt.</summary>
    public string Notes { get; set; } = string.Empty;

    /// <summary>Lowercase hex SHA-256 of the GitHub APK; the app verifies the download against it.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Optional file size (bytes); informational only.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Where the app downloads the APK from — the GitHub "latest" release asset.</summary>
    public string DownloadUrl { get; set; } =
        "https://github.com/lisenhuang/tv-video-hub/releases/latest/download/tv-video-hub.apk";

    /// <summary>Optional ISO-8601 publish timestamp; informational only.</summary>
    public string PublishedAt { get; set; } = string.Empty;
}
