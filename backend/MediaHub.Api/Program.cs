using System.Text.Json;
using System.Text.Json.Serialization;
using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Endpoints;
using MediaHub.Api.Options;
using MediaHub.Api.Storage;

var builder = WebApplication.CreateBuilder(args);

// ---- Options -------------------------------------------------------------
builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));

// ---- JSON: camelCase, ignore nulls, enums as strings --------------------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---- Data + storage ------------------------------------------------------
builder.Services.AddHttpClient<D1Client>();
builder.Services.AddScoped<VideoRepository>();
builder.Services.AddScoped<AppReleaseRepository>();
builder.Services.AddSingleton<R2Storage>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<ApiKeyFilter>();

// Large uploads (videos / apks) via multipart.
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 4L * 1024 * 1024 * 1024; // 4 GB
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors();
app.MapOpenApi();

// Ensure the D1 schema exists at startup (additive, idempotent). Failures here
// shouldn't crash the process — log and continue so health checks still serve.
using (var scope = app.Services.CreateScope())
{
    try
    {
        await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "D1 schema initialization failed; check Cloudflare credentials.");
    }
}

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" })).WithTags("system");

app.MapVideoEndpoints();
app.MapAppEndpoints();

app.Run();

// Exposed for integration testing with WebApplicationFactory.
public partial class Program;
