using MediaHub.Api.Models;

namespace MediaHub.Api.Data;

/// <summary>Catalog persistence, implemented per database provider (D1 or EF Core SQL).</summary>
public interface IVideoRepository
{
    Task<IReadOnlyList<Video>> ListAsync(CancellationToken ct = default);
    Task<Video?> GetAsync(string id, CancellationToken ct = default);
    Task<long> DeleteAsync(string id, CancellationToken ct = default);
    Task InsertAsync(Video v, CancellationToken ct = default);
}

/// <summary>App-release persistence, implemented per database provider (D1 or EF Core SQL).</summary>
public interface IAppReleaseRepository
{
    Task<IReadOnlyList<AppRelease>> ListAsync(CancellationToken ct = default);
    Task<AppRelease?> GetLatestAsync(CancellationToken ct = default);
    Task<AppRelease?> GetByVersionAsync(int versionCode, CancellationToken ct = default);
    Task UpsertAsync(AppRelease r, CancellationToken ct = default);
}

/// <summary>Ensures the schema exists for the configured provider (additive, idempotent).</summary>
public interface ISchemaInitializer
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
}
