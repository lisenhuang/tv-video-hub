using MediaHub.Api.Data;

namespace MediaHub.Api.Settings;

/// <summary>
/// Source of truth for the config that lives IN THE DATABASE: object-storage settings
/// and the release API key. Reads them from the <c>app_config</c> key/value table
/// (via a created scope, since this is a singleton and the DB layer is scoped), caches
/// an immutable snapshot, and reloads lazily after <see cref="Invalidate"/> (called
/// when the dashboard saves). Consumers (<see cref="Storage.S3Storage"/>,
/// <see cref="Auth.ApiKeyFilter"/>) read per operation, so edits take effect without a
/// restart.
///
/// If the DB isn't configured/reachable yet, the snapshot is empty (storage
/// unconfigured, api key blank) and it keeps retrying on the next read — it never
/// throws to the caller.
/// </summary>
public sealed class AppConfigProvider(IServiceScopeFactory scopeFactory)
{
    // app_config keys.
    public const string KeyStorageServiceUrl = "storage.serviceUrl";
    public const string KeyStorageRegion = "storage.region";
    public const string KeyStorageAccessKeyId = "storage.accessKeyId";
    public const string KeyStorageSecretAccessKey = "storage.secretAccessKey";
    public const string KeyStorageVideoBucket = "storage.videoBucket";
    public const string KeyStorageApkBucket = "storage.apkBucket";
    public const string KeyStorageForcePathStyle = "storage.forcePathStyle";
    public const string KeyStoragePresignTtlMinutes = "storage.presignTtlMinutes";
    public const string KeyStorageDisablePayloadSigning = "storage.disablePayloadSigning";
    public const string KeyStorageUseChecksumWhenRequired = "storage.useChecksumWhenRequired";
    public const string KeyStorageProvider = "storage.provider";
    public const string KeyStorageLocalBasePath = "storage.localBasePath";
    public const string KeyStorageLocalSigningKey = "storage.localSigningKey";
    public const string KeyApiKey = "api.key";

    /// <summary>Default filesystem base directory for the local provider.</summary>
    public const string DefaultLocalBasePath = "App_Data/media";

    private readonly SemaphoreSlim _reloadGate = new(1, 1);
    private readonly SemaphoreSlim _signingKeyGate = new(1, 1);
    private readonly object _swap = new();

    private volatile bool _loaded;
    private EffectiveStorageConfig _storage = EmptyStorage();
    private string _apiKey = string.Empty;

    /// <summary>The effective object-storage config (empty if the DB isn't ready yet).</summary>
    public async Task<EffectiveStorageConfig> GetStorageAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        lock (_swap) return _storage;
    }

    /// <summary>The release API key (empty until configured / DB ready).</summary>
    public async Task<string> GetApiKeyAsync(CancellationToken ct = default)
    {
        await EnsureLoadedAsync(ct);
        lock (_swap) return _apiKey;
    }

    /// <summary>The currently-cached storage config without forcing a load (may be empty).</summary>
    public EffectiveStorageConfig CachedStorage { get { lock (_swap) return _storage; } }

    /// <summary>Persist storage/api-key values to the DB and refresh the cache.</summary>
    public async Task SaveAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        await db.TryEnsureSchemaAsync(ct);
        await db.AppConfig.SetManyAsync(values, ct);
        // Force a reload so the just-saved values are reflected in the cache (ReloadAsync
        // no-ops while _loaded is true).
        _loaded = false;
        await ReloadAsync(ct);
    }

    /// <summary>
    /// Return the local HMAC signing key, generating + persisting a random 32-byte key
    /// (base64) on first use. Server-managed; never surfaced in settings responses. The
    /// generated key is saved via the app_config repo and reflected into the cache.
    /// </summary>
    public async Task<string> EnsureLocalSigningKeyAsync(CancellationToken ct = default)
    {
        var existing = (await GetStorageAsync(ct)).LocalSigningKey;
        if (!string.IsNullOrWhiteSpace(existing)) return existing;

        await _signingKeyGate.WaitAsync(ct);
        try
        {
            // Re-check under the gate in case another caller just generated it.
            existing = (await GetStorageAsync(ct)).LocalSigningKey;
            if (!string.IsNullOrWhiteSpace(existing)) return existing;

            var key = Convert.ToBase64String(
                System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            await SaveAsync(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [KeyStorageLocalSigningKey] = key,
            }, ct);
            return key;
        }
        finally
        {
            _signingKeyGate.Release();
        }
    }

    /// <summary>Mark the cache stale so the next read reloads from the DB.</summary>
    public void Invalidate() => _loaded = false;

    private async Task EnsureLoadedAsync(CancellationToken ct)
    {
        if (_loaded) return;
        await ReloadAsync(ct);
    }

    private async Task ReloadAsync(CancellationToken ct)
    {
        await _reloadGate.WaitAsync(ct);
        try
        {
            if (_loaded) return; // another caller just loaded it

            EffectiveStorageConfig storage = EmptyStorage();
            string apiKey = string.Empty;
            var ready = false;

            try
            {
                using var scope = scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
                if (await db.CanConnectAsync(ct))
                {
                    var kv = await db.AppConfig.GetAllAsync(ct);
                    storage = BuildStorage(kv);
                    apiKey = Get(kv, KeyApiKey, string.Empty);
                    ready = true;
                }
            }
            catch
            {
                // DB not ready — leave the snapshot empty and retry next time.
                ready = false;
            }

            lock (_swap)
            {
                _storage = storage;
                _apiKey = apiKey;
            }
            // Only mark loaded once we actually read from a reachable DB; otherwise
            // keep retrying so config appears as soon as the DB comes up.
            _loaded = ready;
        }
        finally
        {
            _reloadGate.Release();
        }
    }

    private static EffectiveStorageConfig BuildStorage(IReadOnlyDictionary<string, string> kv) => new()
    {
        Provider = Get(kv, KeyStorageProvider, "s3"),
        LocalBasePath = Get(kv, KeyStorageLocalBasePath, DefaultLocalBasePath),
        LocalSigningKey = Get(kv, KeyStorageLocalSigningKey, string.Empty),
        ServiceUrl = Get(kv, KeyStorageServiceUrl, string.Empty),
        Region = Get(kv, KeyStorageRegion, "auto"),
        AccessKeyId = Get(kv, KeyStorageAccessKeyId, string.Empty),
        SecretAccessKey = Get(kv, KeyStorageSecretAccessKey, string.Empty),
        VideoBucket = Get(kv, KeyStorageVideoBucket, "videos"),
        ApkBucket = Get(kv, KeyStorageApkBucket, "apks"),
        ForcePathStyle = GetBool(kv, KeyStorageForcePathStyle, true),
        PresignTtlMinutes = GetInt(kv, KeyStoragePresignTtlMinutes, 360),
        DisablePayloadSigning = GetBool(kv, KeyStorageDisablePayloadSigning, true),
        UseChecksumWhenRequired = GetBool(kv, KeyStorageUseChecksumWhenRequired, true),
    };

    private static EffectiveStorageConfig EmptyStorage() => new()
    {
        Provider = "s3",
        LocalBasePath = DefaultLocalBasePath,
        LocalSigningKey = string.Empty,
        ServiceUrl = string.Empty,
        Region = "auto",
        AccessKeyId = string.Empty,
        SecretAccessKey = string.Empty,
        VideoBucket = "videos",
        ApkBucket = "apks",
        ForcePathStyle = true,
        PresignTtlMinutes = 360,
        DisablePayloadSigning = true,
        UseChecksumWhenRequired = true,
    };

    private static string Get(IReadOnlyDictionary<string, string> kv, string key, string fallback) =>
        kv.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;

    private static bool GetBool(IReadOnlyDictionary<string, string> kv, string key, bool fallback) =>
        kv.TryGetValue(key, out var v) && bool.TryParse(v, out var b) ? b : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> kv, string key, int fallback) =>
        kv.TryGetValue(key, out var v) && int.TryParse(v, out var n) && n > 0 ? n : fallback;
}
