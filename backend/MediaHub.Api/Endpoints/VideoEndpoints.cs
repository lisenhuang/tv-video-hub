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
        // Gated by AccessCodeFilter: public while the access-code gate is OFF (default), requires
        // a valid X-Access-Code once an admin turns the gate ON.
        group.MapGet("/", async (VideoRepository repo, CancellationToken ct) =>
        {
            var videos = await repo.ListAsync(ct);
            var dto = new VideoListDto(videos.Select(v => new VideoSummaryDto(
                v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds, v.CreatedAt, v.SizeBytes)).ToList());
            return Results.Ok(dto);
        }).AddEndpointFilter<AccessCodeFilter>();

        // GET /api/videos/{id} — details + presigned playback URL. Same gate as the list.
        group.MapGet("/{id}", async (
            HttpRequest req, string id, VideoRepository repo, StorageRouter r2, CancellationToken ct) =>
        {
            var v = await repo.GetAsync(id, ct);
            if (v is null) return Results.NotFound();

            var videoBucket = await r2.GetVideoBucketAsync(ct);
            // Pass this backend's base URL so local (/api/media/...) playback URLs are absolute.
            var (url, expires) = await r2.GetPresignedGetUrlAsync(
                videoBucket, v.ObjectKey, responseContentType: v.MimeType,
                baseUrl: $"{req.Scheme}://{req.Host}", ct: ct);

            return Results.Ok(new VideoDetailDto(
                v.Id, v.Title, v.Description, v.ThumbnailUrl, v.DurationSeconds,
                url, expires, v.MimeType, v.CreatedAt, v.SizeBytes));
        }).AddEndpointFilter<AccessCodeFilter>();

        // POST /api/videos — register a video (JSON referencing an R2 object, or
        // multipart upload of the bytes). Requires X-Api-Key.
        group.MapPost("/", CreateVideoAsync)
            .AddEndpointFilter<ApiKeyFilter>()
            .DisableAntiforgery();

        return app;
    }

    // Shared create logic lives in VideoCreationService so the admin endpoint can
    // reuse it. This endpoint's behaviour (routes, validation, response) is unchanged.
    private static async Task<IResult> CreateVideoAsync(
        HttpRequest request, VideoCreationService videos, CancellationToken ct)
    {
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(ct);
            var result = await videos.CreateFromUploadAsync(form, ct);
            return result.Video is null
                ? Results.BadRequest(new { error = result.Error })
                : Results.Created($"/api/videos/{result.Video.Id}", result.Video);
        }

        var body = await request.ReadFromJsonAsync<CreateVideoRequest>(ct);
        var refResult = await videos.CreateFromReferenceAsync(body, ct);
        return refResult.Video is null
            ? Results.BadRequest(new { error = refResult.Error })
            : Results.Created($"/api/videos/{refResult.Video.Id}", refResult.Video);
    }
}
