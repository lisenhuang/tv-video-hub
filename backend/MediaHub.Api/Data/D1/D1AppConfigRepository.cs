namespace MediaHub.Api.Data.D1;

/// <summary>
/// Cloudflare D1 implementation of <see cref="IAppConfigRepository"/> over the
/// key/value <c>app_config</c> table.
/// </summary>
public sealed class D1AppConfigRepository(D1Client d1) : IAppConfigRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync("SELECT key, value FROM app_config;", ct: ct);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in rows)
        {
            var key = r.GetString("key");
            if (!string.IsNullOrEmpty(key))
                dict[key] = r.GetString("value") ?? string.Empty;
        }
        return dict;
    }

    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        foreach (var kv in values)
        {
            await d1.ExecuteAsync(
                """
                INSERT INTO app_config (key, value) VALUES (?, ?)
                ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                """,
                [kv.Key, kv.Value],
                ct);
        }
    }
}
