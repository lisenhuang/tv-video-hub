namespace MediaHub.Api.Models;

// DTOs for the admin dashboard API. Serialized camelCase (configured globally).

/// <summary>
/// First-run wizard state. <c>needsAdmin</c> → create the local admin; then after
/// login <c>needsDatabase</c>/<c>needsStorage</c> drive the rest of setup.
/// <c>needsSetup</c> is kept (== needsAdmin) for backward-compatible clients.
/// </summary>
public sealed record SetupStateDto(
    bool NeedsSetup,
    bool NeedsAdmin,
    bool Authenticated,
    bool NeedsDatabase,
    bool NeedsStorage);

public sealed record CredentialsRequest(string? Username, string? Password);

public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

public sealed record AdminIdentityDto(string Username);

/// <summary>Admin view of a release (read-only list), reusing the public shape's fields.</summary>
public sealed record AdminReleaseDto(
    int VersionCode,
    string VersionName,
    string Notes,
    string ObjectKey,
    long SizeBytes,
    string Sha256,
    int MinSdk,
    DateTimeOffset PublishedAt);

public sealed record AdminReleaseListDto(IReadOnlyList<AdminReleaseDto> Releases);

public sealed record AdminVideoListDto(IReadOnlyList<VideoSummaryDto> Videos);

// ---- Settings ------------------------------------------------------------

/// <summary>A secret field shown to the dashboard: never the value, only whether
/// it is set and its last few characters as a hint.</summary>
public sealed record MaskedSecretDto(bool IsSet, string? Last4);

/// <summary>
/// Current effective config with secrets masked. Three groups: pluggable database
/// (D1 or self-hosted SQL), S3-compatible object storage, and the release API key.
/// </summary>
public sealed record SettingsViewDto(
    // Database (pluggable)
    string DatabaseProvider,
    string AccountId,
    string D1DatabaseId,
    MaskedSecretDto D1ApiToken,
    MaskedSecretDto DatabaseConnectionString,
    bool DatabaseConfigured,
    // Object storage (S3-compatible)
    string StorageServiceUrl,
    string StorageRegion,
    MaskedSecretDto StorageAccessKeyId,
    MaskedSecretDto StorageSecretAccessKey,
    string StorageVideoBucket,
    string StorageApkBucket,
    bool StorageForcePathStyle,
    int StoragePresignTtlMinutes,
    bool StorageDisablePayloadSigning,
    bool StorageUseChecksumWhenRequired,
    bool StorageConfigured,
    // Release write secret
    MaskedSecretDto ApiKey);

/// <summary>
/// Editable settings payload. Blank/absent secret fields mean "leave unchanged".
/// Non-secret string fields are applied as given (blank clears the value). Nullable
/// bool/int fields left null mean "leave unchanged".
/// </summary>
public sealed record SettingsUpdateRequest(
    // Database (pluggable)
    string? DatabaseProvider,
    string? AccountId,
    string? D1DatabaseId,
    string? D1ApiToken,
    string? DatabaseConnectionString,
    // Object storage (S3-compatible)
    string? StorageServiceUrl,
    string? StorageRegion,
    string? StorageAccessKeyId,
    string? StorageSecretAccessKey,
    string? StorageVideoBucket,
    string? StorageApkBucket,
    bool? StorageForcePathStyle,
    int? StoragePresignTtlMinutes,
    bool? StorageDisablePayloadSigning,
    bool? StorageUseChecksumWhenRequired,
    // Release write secret
    string? ApiKey);

public sealed record ConnectionResultDto(bool Ok, string Message);

public sealed record SettingsTestDto(ConnectionResultDto Database, ConnectionResultDto Storage);
