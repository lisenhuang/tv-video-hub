namespace MediaHub.Api.Options;

/// <summary>
/// Where the runtime-editable Cloudflare settings file lives. Bound from the
/// "Settings" config section. The default path is resolved relative to the app's
/// content root when not absolute.
/// </summary>
public sealed class SettingsOptions
{
    public const string SectionName = "Settings";

    /// <summary>
    /// Path to the JSON file that persists dashboard-edited Cloudflare config.
    /// Defaults to <c>App_Data/cloudflare.settings.json</c> under the content root.
    /// </summary>
    public string FilePath { get; set; } = "App_Data/cloudflare.settings.json";
}
