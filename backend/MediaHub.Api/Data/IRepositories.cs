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

/// <summary>Admin-account persistence (single-admin), implemented per provider.</summary>
public interface IAdminRepository
{
    Task<long> CountAsync(CancellationToken ct = default);
    Task<Admin?> GetByUsernameAsync(string username, CancellationToken ct = default);
    Task InsertAsync(Admin admin, CancellationToken ct = default);
    Task UpdatePasswordAsync(string username, string passwordHash, string passwordSalt, CancellationToken ct = default);
}

/// <summary>
/// Key/value config persistence for settings that live in the DB (object storage +
/// release API key), implemented per provider over the <c>app_config</c> table.
/// </summary>
public interface IAppConfigRepository
{
    Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default);
    Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken ct = default);
}

/// <summary>Ensures the schema exists for the configured provider (additive, idempotent).</summary>
public interface ISchemaInitializer
{
    Task EnsureSchemaAsync(CancellationToken ct = default);
}
