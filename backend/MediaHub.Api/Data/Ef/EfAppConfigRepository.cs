using MediaHub.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace MediaHub.Api.Data.Ef;

/// <summary>EF Core implementation of <see cref="IAppConfigRepository"/> (the <c>app_config</c> table).</summary>
public sealed class EfAppConfigRepository(EfContextFactory factory) : IAppConfigRepository
{
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = factory.Create();
        var rows = await db.AppConfig.AsNoTracking().ToListAsync(ct);
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in rows) dict[e.Key] = e.Value;
        return dict;
    }

    public async Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        if (values.Count == 0) return;

        await using var db = factory.Create();
        var keys = values.Keys.ToList();
        var existing = await db.AppConfig.Where(c => keys.Contains(c.Key)).ToListAsync(ct);
        var byKey = existing.ToDictionary(c => c.Key, StringComparer.Ordinal);

        foreach (var kv in values)
        {
            if (byKey.TryGetValue(kv.Key, out var entry))
                entry.Value = kv.Value;
            else
                db.AppConfig.Add(new AppConfigEntry { Key = kv.Key, Value = kv.Value });
        }
        await db.SaveChangesAsync(ct);
    }
}
