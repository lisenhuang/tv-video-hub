using System.Text.Json;
using System.Text.Json.Serialization;
using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Endpoints;
using MediaHub.Api.Options;
using MediaHub.Api.Settings;
using MediaHub.Api.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---- Options -------------------------------------------------------------
builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<SettingsOptions>(
    builder.Configuration.GetSection(SettingsOptions.SectionName));

// ---- JSON: camelCase, ignore nulls, enums as strings --------------------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---- Runtime-editable Cloudflare settings -------------------------------
// SettingsStore persists dashboard edits to a JSON file; the provider merges them
// over the env/appsettings defaults and is the live source read by D1Client/R2Storage.
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<CloudflareSettingsProvider>();

// ---- Data + storage ------------------------------------------------------
builder.Services.AddHttpClient<D1Client>();
builder.Services.AddScoped<VideoRepository>();
builder.Services.AddScoped<AppReleaseRepository>();
builder.Services.AddScoped<AdminRepository>();
builder.Services.AddScoped<VideoCreationService>();
builder.Services.AddSingleton<R2Storage>();
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddScoped<ApiKeyFilter>();

// ---- Admin cookie authentication (additional to the X-Api-Key path) ------
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "mediahub.admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.SlidingExpiration = true;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.LoginPath = "/admin";
        options.LogoutPath = "/admin";

        // API calls must get a JSON 401, not an HTML redirect to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

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

// Serve the admin dashboard's static assets from wwwroot.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

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
app.MapAdminEndpoints();

// Serve the admin dashboard SPA at /admin (and /admin/* deep links). This does
// not intercept /api/* routes.
app.MapGet("/admin", () => ServeAdminIndex(app.Environment)).ExcludeFromDescription();
app.MapGet("/admin/{**rest}", () => ServeAdminIndex(app.Environment)).ExcludeFromDescription();

app.Run();

static IResult ServeAdminIndex(IWebHostEnvironment env)
{
    var path = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
        "admin", "index.html");
    return File.Exists(path)
        ? Results.File(path, "text/html")
        : Results.NotFound();
}

// Exposed for integration testing with WebApplicationFactory.
public partial class Program;
