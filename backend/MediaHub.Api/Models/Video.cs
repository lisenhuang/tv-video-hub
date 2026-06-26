namespace MediaHub.Api.Models;

/// <summary>A catalog video as persisted in D1 (the <c>videos</c> table).</summary>
public sealed class Video
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Object key inside the R2 video bucket.</summary>
    public string ObjectKey { get; set; } = string.Empty;

    public string? ThumbnailUrl { get; set; }
    public int? DurationSeconds { get; set; }
    public string MimeType { get; set; } = "video/mp4";
    public DateTimeOffset CreatedAt { get; set; }
}
