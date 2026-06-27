namespace MediaHub.Api.Options;

/// <summary>
/// Describes the APK that the Android app self-updates to. The APK is committed INTO this
/// backend (a single file at <c>wwwroot/app/app-release.apk</c>) and served directly as a
/// static file — no GitHub Releases, no CI publish step. This class is just the metadata the
/// version-check endpoint (<c>GET /api/app/latest</c>) returns: the app compares
/// <see cref="VersionCode"/> to its own build and, if newer, downloads the APK and verifies
/// <see cref="Sha256"/>.
///
/// Bound from the "AppRelease" config section (appsettings / env). Whenever you rebuild the
/// APK and commit it over <c>wwwroot/app/app-release.apk</c>, bump <see cref="VersionCode"/>
/// and set <see cref="Sha256"/>/<see cref="SizeBytes"/> to that committed file's values
/// (since the served file IS the built file, the hash always matches — no CI to wait on).
/// While <see cref="VersionCode"/> equals the installed build the app shows no update (and
/// never checks the hash), so a blank <see cref="Sha256"/> is fine until the first real bump.
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

    /// <summary>Lowercase hex SHA-256 of the committed APK; the app verifies the download against it.</summary>
    public string Sha256 { get; set; } = string.Empty;

    /// <summary>Optional file size (bytes); informational only.</summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// Server-relative path of the committed APK static file. <c>GET /api/app/latest</c>
    /// turns this into an absolute <c>downloadUrl</c> by prefixing the request's own base
    /// (scheme + host + path-base) — so the app downloads from the SAME backend it asked,
    /// with no host hard-coded here and no app change needed. Keep it pointing at the static
    /// file under <c>wwwroot</c> (default <c>/app/app-release.apk</c>).
    /// </summary>
    public string DownloadPath { get; set; } = "/app/app-release.apk";

    /// <summary>
    /// Optional absolute override for the download URL. Leave blank to use <see cref="DownloadPath"/>
    /// composed against the request base (the normal case). Set it only to host the APK somewhere
    /// else entirely, or to pin an exact public URL when behind a proxy that rewrites scheme/host.
    /// </summary>
    public string DownloadUrl { get; set; } = string.Empty;

    /// <summary>Optional ISO-8601 publish timestamp; informational only.</summary>
    public string PublishedAt { get; set; } = string.Empty;

    /// <summary>
    /// When true the update is MANDATORY: a gate-aware app must not let the user dismiss/cancel
    /// the update prompt (no "Later"). Older apps that don't know this field simply ignore it and
    /// behave as before, so it's safe/additive.
    /// </summary>
    public bool ForceUpdate { get; set; }
}
