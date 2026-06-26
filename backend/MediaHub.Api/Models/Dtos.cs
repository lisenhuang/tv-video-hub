namespace MediaHub.Api.Models;

// Response/request shapes mirror the contract documented in the repo root README.
// System.Text.Json serializes these as camelCase (configured in Program.cs).

public sealed record VideoSummaryDto(
    string Id,
    string Title,
    string? Description,
    string? ThumbnailUrl,
    int? DurationSeconds,
    DateTimeOffset CreatedAt);

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
    DateTimeOffset CreatedAt);

/// <summary>JSON body for registering a video that already exists in R2.</summary>
public sealed record CreateVideoRequest(
    string Title,
    string? Description,
    string ObjectKey,
    string? ThumbnailUrl,
    int? DurationSeconds,
    string? MimeType);

public sealed record AppReleaseDto(
    int VersionCode,
    string VersionName,
    string Notes,
    string DownloadUrl,
    long SizeBytes,
    string Sha256,
    int MinSdk,
    DateTimeOffset PublishedAt);
