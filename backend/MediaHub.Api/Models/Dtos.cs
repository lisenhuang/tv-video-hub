namespace MediaHub.Api.Models;

// Response/request shapes mirror the contract documented in the repo root README.
// System.Text.Json serializes these as camelCase (configured in Program.cs).

// SizeBytes is appended last (additive): new optional fields go at the end so the
// record's existing positional shape is untouched. JSON is matched by name regardless.
public sealed record VideoSummaryDto(
    string Id,
    string Title,
    string? Description,
    string? ThumbnailUrl,
    int? DurationSeconds,
    DateTimeOffset CreatedAt,
    long? SizeBytes = null);

public sealed record VideoListDto(IReadOnlyList<VideoSummaryDto> Videos);

public sealed record VideoDetailDto(
    string Id,
    string Title,
    string? Description,
    string? ThumbnailUrl,
    int? DurationSeconds,
    string PlaybackUrl,
    DateTimeOffset PlaybackUrlExpiresAt,
    string MimeType,
    DateTimeOffset CreatedAt,
    long? SizeBytes = null);

/// <summary>JSON body for registering a video that already exists in R2.</summary>
public sealed record CreateVideoRequest(
    string Title,
    string? Description,
    string ObjectKey,
    string? ThumbnailUrl,
    int? DurationSeconds,
    string? MimeType,
    long? SizeBytes = null);

public sealed record AppReleaseDto(
    int VersionCode,
    string VersionName,
    string Notes,
    string DownloadUrl,
    long SizeBytes,
    string Sha256,
    int MinSdk,
    DateTimeOffset PublishedAt,
    bool ForceUpdate);

/// <summary>
/// Whether the app must present an access code, and whether the one it sent (header
/// <c>X-Access-Code</c>) is currently valid. <c>required=false</c> means the gate is off and the
/// app should proceed straight to content.
/// </summary>
public sealed record AccessStatusDto(bool Required, bool Valid);
