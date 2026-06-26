using MediaHub.Api.Models;
using MediaHub.Api.Storage;

namespace MediaHub.Api.Data;

/// <summary>
/// Shared video-creation logic reused by both the public X-Api-Key
/// <c>POST /api/videos</c> endpoint and the cookie-authed admin endpoint, so the
/// two stay behaviourally identical. Returns either the created video summary or a
/// validation error message.
/// </summary>
public sealed class VideoCreationService(VideoRepository repo, StorageRouter storage)
{
    public sealed record Result(VideoSummaryDto? Video, string? Error)
    {
        public static Result Ok(VideoSummaryDto v) => new(v, null);
        public static Result Fail(string error) => new(null, error);
    }

    /// <summary>Handle a multipart upload (a <c>file</c> part) → store in R2 + record in D1.</summary>
    public async Task<Result> CreateFromUploadAsync(IFormCollection form, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        var file = form.Files["file"];
        if (file is null || file.Length == 0)
            return Result.Fail("multipart upload requires a 'file' part.");

        var title = form["title"].ToString();
        if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(file.FileName);

        var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "video/mp4" : file.ContentType;
        var key = $"videos/{id}/{SanitizeFileName(file.FileName)}";

        var videoBucket = await storage.GetVideoBucketAsync(ct);
        await using (var stream = file.OpenReadStream())
            await storage.PutAsync(videoBucket, key, stream, mime, ct);

        var uploaded = new Video
        {
            Id = id,
            Title = title,
            Description = NullIfEmpty(form["description"]),
            ObjectKey = key,
            ThumbnailUrl = NullIfEmpty(form["thumbnailUrl"]),
            DurationSeconds = int.TryParse(form["durationSeconds"], out var d) ? d : null,
            MimeType = mime,
            CreatedAt = now,
        };
        await repo.InsertAsync(uploaded, ct);
        return Result.Ok(ToSummary(uploaded));
    }

    /// <summary>Register a video that already exists in R2 (JSON body referencing an object key).</summary>
    public async Task<Result> CreateFromReferenceAsync(CreateVideoRequest? body, CancellationToken ct)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.ObjectKey) || string.IsNullOrWhiteSpace(body.Title))
            return Result.Fail("title and objectKey are required.");

        var videoBucket = await storage.GetVideoBucketAsync(ct);
        if (!await storage.ExistsAsync(videoBucket, body.ObjectKey, ct))
            return Result.Fail($"object '{body.ObjectKey}' not found in video bucket.");

        var video = new Video
        {
            Id = Guid.NewGuid().ToString("n"),
            Title = body.Title,
            Description = body.Description,
            ObjectKey = body.ObjectKey,
            ThumbnailUrl = body.ThumbnailUrl,
            DurationSeconds = body.DurationSeconds,
            MimeType = string.IsNullOrWhiteSpace(body.MimeType) ? "video/mp4" : body.MimeType,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await repo.InsertAsync(video, ct);
        return Result.Ok(ToSummary(video));
    }

    /// <summary>Delete a video's D1 row and best-effort delete its R2 object.</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken ct)
    {
        var video = await repo.GetAsync(id, ct);
        if (video is null) return false;

        var deleted = await repo.DeleteAsync(id, ct);

        // Best-effort storage cleanup: the row is gone regardless. We delete after the
        // row so a storage hiccup can't leave a dangling catalog entry. Provider-agnostic
        // (S3 or local), so swallow any storage error here.
        try
        {
            var videoBucket = await storage.GetVideoBucketAsync(ct);
            await storage.DeleteAsync(videoBucket, video.ObjectKey, ct);
        }
        catch
        {
            // Object may already be absent or temporarily unavailable; the catalog
            // entry is removed either way.
        }

        return deleted > 0;
    }

    public static VideoSummaryDto ToSummary(Video v) =>
        new(v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds, v.CreatedAt);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return string.IsNullOrWhiteSpace(cleaned) ? "video.mp4" : cleaned;
    }
}
