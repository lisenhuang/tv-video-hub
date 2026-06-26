using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MediaHub.Api.Settings;

namespace MediaHub.Api.Data;

/// <summary>
/// Thin client over the Cloudflare D1 HTTP query API:
/// <c>POST /accounts/{account}/d1/database/{db}/query</c> with a parameterized
/// SQL statement. D1 is SQLite under the hood, so we use <c>?</c> placeholders
/// and positional params.
///
/// The Cloudflare credentials are resolved per request from
/// <see cref="SettingsProvider"/> rather than captured once in the constructor,
/// so dashboard edits take effect without a restart. The injected
/// <see cref="HttpClient"/> is used without a fixed BaseAddress: each request
/// targets an absolute URI built from the current settings.
/// </summary>
public sealed class D1Client
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly SettingsProvider _settings;
    private readonly ILogger<D1Client> _log;

    public D1Client(HttpClient http, SettingsProvider settings, ILogger<D1Client> log)
    {
        _http = http;
        _settings = settings;
        _log = log;
    }

    private static string BuildQueryUri(EffectiveCloudflareConfig cf) =>
        $"{cf.D1ApiBaseUrl.TrimEnd('/')}/accounts/{cf.AccountId}/d1/database/{cf.D1DatabaseId}/query";

    private static HttpRequestMessage BuildRequest(EffectiveCloudflareConfig cf, D1QueryRequest payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, BuildQueryUri(cf))
        {
            Content = JsonContent.Create(payload, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cf.D1ApiToken);
        return request;
    }

    /// <summary>Execute a statement and return the first result set's rows.</summary>
    public async Task<IReadOnlyList<D1Row>> QueryAsync(
        string sql, IReadOnlyList<object?>? parameters = null, CancellationToken ct = default)
    {
        var cf = _settings.Cloudflare;
        var payload = new D1QueryRequest(sql, NormalizeParams(parameters));

        using var request = BuildRequest(cf, payload);
        using var resp = await _http.SendAsync(request, ct);
        var body = await resp.Content.ReadFromJsonAsync<D1QueryEnvelope>(JsonOptions, ct);

        if (!resp.IsSuccessStatusCode || body is null || !body.Success)
        {
            var detail = body?.Errors is { Count: > 0 }
                ? string.Join("; ", body.Errors.Select(e => $"{e.Code}:{e.Message}"))
                : $"HTTP {(int)resp.StatusCode}";
            _log.LogError("D1 query failed ({Detail}) for SQL: {Sql}", detail, sql);
            throw new D1Exception($"D1 query failed: {detail}");
        }

        var first = body.Result.FirstOrDefault();
        return first?.Results ?? [];
    }

    /// <summary>Execute a statement that returns no rows; yields rows affected.</summary>
    public async Task<long> ExecuteAsync(
        string sql, IReadOnlyList<object?>? parameters = null, CancellationToken ct = default)
    {
        var cf = _settings.Cloudflare;
        var payload = new D1QueryRequest(sql, NormalizeParams(parameters));

        using var request = BuildRequest(cf, payload);
        using var resp = await _http.SendAsync(request, ct);
        var body = await resp.Content.ReadFromJsonAsync<D1QueryEnvelope>(JsonOptions, ct);

        if (!resp.IsSuccessStatusCode || body is null || !body.Success)
        {
            var detail = body?.Errors is { Count: > 0 }
                ? string.Join("; ", body.Errors.Select(e => $"{e.Code}:{e.Message}"))
                : $"HTTP {(int)resp.StatusCode}";
            _log.LogError("D1 execute failed ({Detail}) for SQL: {Sql}", detail, sql);
            throw new D1Exception($"D1 execute failed: {detail}");
        }

        return body.Result.FirstOrDefault()?.Meta?.ChangesCount ?? 0;
    }

    // D1 binds params positionally; booleans/dates must be primitive JSON values.
    private static List<object?> NormalizeParams(IReadOnlyList<object?>? parameters)
    {
        var list = new List<object?>();
        if (parameters is null) return list;
        foreach (var p in parameters)
        {
            list.Add(p switch
            {
                null => null,
                DateTimeOffset dto => dto.ToUniversalTime().ToString("O"),
                DateTime dt => dt.ToUniversalTime().ToString("O"),
                bool b => b ? 1 : 0,
                _ => p
            });
        }
        return list;
    }

    private sealed record D1QueryRequest(
        [property: JsonPropertyName("sql")] string Sql,
        [property: JsonPropertyName("params")] IReadOnlyList<object?> Params);

    private sealed class D1QueryEnvelope
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("result")] public List<D1ResultSet> Result { get; set; } = [];
        [JsonPropertyName("errors")] public List<D1Error> Errors { get; set; } = [];
    }

    private sealed class D1ResultSet
    {
        [JsonPropertyName("results")] public List<D1Row> Results { get; set; } = [];
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("meta")] public D1Meta? Meta { get; set; }
    }

    private sealed class D1Meta
    {
        [JsonPropertyName("changes")] public long ChangesCount { get; set; }
        [JsonPropertyName("last_row_id")] public long LastRowId { get; set; }
    }

    private sealed class D1Error
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    }
}

/// <summary>A single D1 result row: column name → JSON value.</summary>
public sealed class D1Row : Dictionary<string, JsonElement>
{
    public string? GetString(string col) =>
        TryGetValue(col, out var v) && v.ValueKind is not JsonValueKind.Null
            ? v.GetString()
            : null;

    public string GetRequiredString(string col) =>
        GetString(col) ?? throw new D1Exception($"Column '{col}' was null or missing.");

    public int? GetInt(string col) =>
        TryGetValue(col, out var v) && v.ValueKind is JsonValueKind.Number
            ? v.GetInt32()
            : v.ValueKind is JsonValueKind.String && int.TryParse(v.GetString(), out var n) ? n : null;

    public long GetLong(string col) =>
        TryGetValue(col, out var v) && v.ValueKind is JsonValueKind.Number
            ? v.GetInt64()
            : v.ValueKind is JsonValueKind.String && long.TryParse(v.GetString(), out var n) ? n : 0;

    public DateTimeOffset GetDate(string col) =>
        DateTimeOffset.TryParse(GetString(col), out var d) ? d : default;
}

public sealed class D1Exception(string message) : Exception(message);
