using System.Text.Json;
using MediaHub.Api.Options;
using Microsoft.Extensions.Options;

namespace MediaHub.Api.Settings;

/// <summary>
/// Reads and writes the ONLY on-disk config — the database connection — as a small
/// JSON file under the content root (default <c>App_Data/db.json</c>). Thread-safe;
/// the directory is created on demand. A missing/unreadable file yields an empty
/// (all-null) config, so the app boots unconfigured and the dashboard drives setup.
/// Admin/storage/api-key are NOT here — they live in the database.
/// </summary>
public sealed class DbConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly ILogger<DbConfigStore> _log;
    private readonly object _gate = new();

    public DbConfigStore(
        IOptions<SettingsOptions> options,
        IHostEnvironment env,
        ILogger<DbConfigStore> log)
    {
        _log = log;

        var configured = options.Value.FilePath;
        if (string.IsNullOrWhiteSpace(configured))
            configured = "App_Data/db.json";

        _filePath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(env.ContentRootPath, configured);
    }

    public string FilePath => _filePath;

    /// <summary>Load the on-disk DB config, or an empty object if none exists.</summary>
    public DatabaseFileConfig Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(_filePath))
                    return new DatabaseFileConfig();

                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new DatabaseFileConfig();

                return JsonSerializer.Deserialize<DatabaseFileConfig>(json, JsonOptions)
                       ?? new DatabaseFileConfig();
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Failed to read DB-config file at {Path}; treating as unconfigured.", _filePath);
                return new DatabaseFileConfig();
            }
        }
    }

    /// <summary>Persist the DB config, creating the directory if needed.</summary>
    public void Save(DatabaseFileConfig config)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(config, JsonOptions);
            File.WriteAllText(_filePath, json);
            _log.LogInformation("Persisted DB config to {Path}.", _filePath);
        }
    }
}
