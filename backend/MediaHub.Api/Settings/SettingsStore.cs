using System.Text.Json;
using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Reads and writes the runtime-editable settings (Cloudflare D1 + S3-compatible
/// object storage) as a JSON file under the content root. Thread-safe; the
/// directory is created on demand. Missing or unreadable files yield an empty
/// (all-null) settings object so the app falls back entirely to the
/// env/appsettings defaults.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly ILogger<SettingsStore> _log;
    private readonly object _gate = new();

    public SettingsStore(
        IOptions<SettingsOptions> options,
        IHostEnvironment env,
        ILogger<SettingsStore> log)
    {
        _log = log;

        var configured = options.Value.FilePath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = "App_Data/settings.json";

        _filePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    public string FilePath => _filePath;

    /// <summary>Load the persisted overrides, or an empty object if none exist.</summary>
    public PersistedSettings Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new PersistedSettings();

                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new PersistedSettings();

                return JsonSerializer.Deserialize<PersistedSettings>(json, JsonOptions)
                       ?? new PersistedSettings();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to read settings file at {Path}; using defaults.", _filePath);
                return new PersistedSettings();
            }
        }
    }

    /// <summary>Persist the overrides, creating the directory if needed.</summary>
    public void Save(PersistedSettings settings)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(_filePath, json);
            _log.LogInformation("Persisted runtime settings to {Path}.", _filePath);
        }
    }
}
