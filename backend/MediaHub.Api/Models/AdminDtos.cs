namespace MediaHub.Api.Models;

// DTOs for the admin dashboard API. Serialized camelCase (configured globally).

public sealed record SetupStateDto(bool NeedsSetup, bool Authenticated);

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

/// <summary>Current effective Cloudflare config with secrets masked.</summary>
public sealed record SettingsViewDto(
    string AccountId,
    string D1DatabaseId,
    MaskedSecretDto D1ApiToken,
    MaskedSecretDto R2AccessKeyId,
    MaskedSecretDto R2SecretAccessKey,
    string R2VideoBucket,
    string R2ApkBucket,
    string R2ServiceUrl,
    int R2PresignTtlMinutes);

/// <summary>
/// Editable settings payload. Blank/absent secret fields mean "leave unchanged".
/// Non-secret fields are applied as given (blank clears the override → falls back
/// to the env/appsettings default).
/// </summary>
public sealed record SettingsUpdateRequest(
    string? AccountId,
    string? D1DatabaseId,
    string? D1ApiToken,
    string? R2AccessKeyId,
    string? R2SecretAccessKey,
    string? R2VideoBucket,
    string? R2ApkBucket,
    string? R2ServiceUrl,
    int? R2PresignTtlMinutes);

public sealed record ConnectionResultDto(bool Ok, string Message);

public sealed record SettingsTestDto(ConnectionResultDto D1, ConnectionResultDto R2);
