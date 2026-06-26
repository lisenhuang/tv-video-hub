using MediaHub.Api.Models;

namespace MediaHub.Api.Data;

public sealed class VideoRepository(D1Client d1)
{
    public async Task<IReadOnlyList<Video>> ListAsync(CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM videos ORDER BY created_at DESC;", ct: ct);
        return rows.Select(Map).ToList();
    }

    public async Task<Video?> GetAsync(string id, CancellationToken ct = default)
    {
        var rows = await d1.QueryAsync(
            "SELECT * FROM videos WHERE id = ? LIMIT 1;", [id], ct);
        return rows.Count == 0 ? null : Map(rows[0]);
    }

    public async Task<long> DeleteAsync(string id, CancellationToken ct = default)
    {
        return await d1.ExecuteAsync("DELETE FROM videos WHERE id = ?;", [id], ct);
    }

    public async Task InsertAsync(Video v, CancellationToken ct = default)
    {
        await d1.ExecuteAsync(
            """
            INSERT INTO videos
                (id, title, description, object_key, thumbnail_url, duration_seconds, mime_type, created_at)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?);
            """,
            [v.Id, v.Title, v.Description, v.ObjectKey, v.ThumbnailUrl,
             v.DurationSeconds, v.MimeType, v.CreatedAt],
            ct);
    }

    private static Video Map(D1Row r) => new()
    {
        Id = r.GetRequiredString("id"),
        Title = r.GetRequiredString("title"),
        Description = r.GetString("description"),
        ObjectKey = r.GetRequiredString("object_key"),
        ThumbnailUrl = r.GetString("thumbnail_url"),
        DurationSeconds = r.GetInt("duration_seconds"),
        MimeType = r.GetString("mime_type") ?? "video/mp4",
        CreatedAt = r.GetDate("created_at"),
    };
}
