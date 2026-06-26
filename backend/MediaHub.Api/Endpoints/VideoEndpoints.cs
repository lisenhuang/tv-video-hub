using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Models;
using MediaHub.Api.Storage;

namespace MediaHub.Api.Endpoints;

public static class VideoEndpoints
{
    public static IEndpointRouteBuilder MapVideoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/videos").WithTags("videos");

        // GET /api/videos — list catalog, newest first.
        group.MapGet("/", async (VideoRepository repo, CancellationToken ct) =>
        {
            var videos = await repo.ListAsync(ct);
            var dto = new VideoListDto(videos.Select(v => new VideoSummaryDto(
                v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds, v.CreatedAt)).ToList());
            return Results.Ok(dto);
        });

        // GET /api/videos/{id} — details + presigned playback URL.
        group.MapGet("/{id}", async (
            string id, VideoRepository repo, R2Storage r2, CancellationToken ct) =>
        {
            var v = await repo.GetAsync(id, ct);
            if (v is null) return Results.NotFound();

            var (url, expires) = r2.GetPresignedGetUrl(
                r2.VideoBucket, v.ObjectKey, responseContentType: v.MimeType);

            return Results.Ok(new VideoDetailDto(
                v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds,
                url, expires, v.MimeType, v.CreatedAt));
        });

        // POST /api/videos — register a video (JSON referencing an R2 object, or
        // multipart upload of the bytes). Requires X-Api-Key.
        group.MapPost("/", CreateVideoAsync)
            .AddEndpointFilter<ApiKeyFilter>()
            .DisableAntiforgery();

        return app;
    }

    private static async Task<IResult> CreateVideoAsync(HttpRequest request, VideoRepository repo, R2Storage r2, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("n");
        var now = DateTimeOffset.UtcNow;

        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var file = form.Files["file"];
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "multipart upload requires a 'file' part." });

            var title = form["title"].ToString();
            if (string.IsNullOrWhiteSpace(title)) title = Path.GetFileNameWithoutExtension(file.FileName);

            var mime = string.IsNullOrWhiteSpace(file.ContentType) ? "video/mp4" : file.ContentType;
            var key = $"videos/{id}/{SanitizeFileName(file.FileName)}";

            await using (var stream = file.OpenReadStream())
                await r2.PutAsync(r2.VideoBucket, key, stream, mime, ct);

            var uploaded = new Video
            {
                Id = id,
                Title = title,
                Description = form["description"],
                ObjectKey = key,
                ThumbnailUrl = NullIfEmpty(form["thumbnailUrl"]),
                DurationSeconds = int.TryParse(form["durationSeconds"], out var d) ? d : null,
                MimeType = mime,
                CreatedAt = now,
            };
            await repo.InsertAsync(uploaded, ct);
            return Results.Created($"/api/videos/{id}", ToSummary(uploaded));
        }

        var body = await request.ReadFromJsonAsync<CreateVideoRequest>(ct);
        if (body is null || string.IsNullOrWhiteSpace(body.ObjectKey) || string.IsNullOrWhiteSpace(body.Title))
            return Results.BadRequest(new { error = "title and objectKey are required." });

        if (!await r2.ExistsAsync(r2.VideoBucket, body.ObjectKey, ct))
            return Results.BadRequest(new { error = $"object '{body.ObjectKey}' not found in video bucket." });

        var video = new Video
        {
            Id = id,
            Title = body.Title,
            Description = body.Description,
            ObjectKey = body.ObjectKey,
            ThumbnailUrl = body.ThumbnailUrl,
            DurationSeconds = body.DurationSeconds,
            MimeType = string.IsNullOrWhiteSpace(body.MimeType) ? "video/mp4" : body.MimeType,
            CreatedAt = now,
        };
        await repo.InsertAsync(video, ct);
        return Results.Created($"/api/videos/{id}", ToSummary(video));
    }

    private static VideoSummaryDto ToSummary(Video v) =>
        new(v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds, v.CreatedAt);

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string SanitizeFileName(string name)
    {
        var cleaned = string.Concat(name.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));
        return string.IsNullOrWhiteSpace(cleaned) ? "video.mp4" : cleaned;
    }
}
