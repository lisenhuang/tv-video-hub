namespace MediaHub.Api.Options;

/// <summary>
/// Where the on-disk <b>database-connection</b> config file lives — the ONLY thing
/// stored on local disk (admin/storage/api-key live in the database). Bound from the
/// "Settings" config section; the path is resolved relative to the content root when
/// not absolute.
/// </summary>
public sealed class SettingsOptions
{
    public const string SectionName = "Settings";

    /// <summary>
    /// Path to the JSON file that persists the database connection config.
    /// Defaults to <c>App_Data/db.json</c> under the content root.
    /// </summary>
    public string FilePath { get; set; } = "App_Data/db.json";
}
