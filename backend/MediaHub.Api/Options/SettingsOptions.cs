namespace MediaHub.Api.Options;

/// <summary>
/// Where the local settings file lives. This file is the single source of bootstrap
/// truth (admin account + database/storage/api config). Bound from the "Settings"
/// config section; the path is resolved relative to the content root when not absolute.
/// </summary>
public sealed class SettingsOptions
{
    public const string SectionName = "Settings";

    /// <summary>
    /// Path to the JSON file that persists all dashboard-edited config.
    /// Defaults to <c>App_Data/settings.json</c> under the content root.
    /// </summary>
    public string FilePath { get; set; } = "App_Data/settings.json";
}
