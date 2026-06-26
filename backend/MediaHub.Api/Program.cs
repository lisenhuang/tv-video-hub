using System.Text.Json;
using System.Text.Json.Serialization;
using MediaHub.Api.Auth;
using MediaHub.Api.Data;
using MediaHub.Api.Data.Ef;
using MediaHub.Api.Endpoints;
using MediaHub.Api.Options;
using MediaHub.Api.Settings;
using MediaHub.Api.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;

var builder = WebApplication.CreateBuilder(args);

// ---- Options (OPTIONAL env/appsettings seeds; the local file is authoritative) --
builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<ApiOptions>(
    builder.Configuration.GetSection(ApiOptions.SectionName));
builder.Services.Configure<StorageOptions>(
    builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<SettingsOptions>(
    builder.Configuration.GetSection(SettingsOptions.SectionName));

// ---- JSON: camelCase, ignore nulls, enums as strings --------------------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---- Runtime-editable settings (Cloudflare D1 + S3 object storage) ------
// SettingsStore persists dashboard edits to a JSON file; SettingsProvider merges
// them over the env/appsettings defaults and is the live source read per-operation
// by D1Client (Cloudflare D1) and S3Storage (S3-compatible object storage).
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<SettingsProvider>();

// ---- Data + storage ------------------------------------------------------
// Pluggable database: D1 (HTTP) or EF Core SQL (sqlite/postgres/mysql/sqlserver),
// chosen at runtime from settings. DatabaseService resolves the right impl per scope;
// the VideoRepository/AppReleaseRepository facades ensure schema + delegate.
builder.Services.AddHttpClient<D1Client>();
builder.Services.AddSingleton<EfContextFactory>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<VideoRepository>();
builder.Services.AddScoped<AppReleaseRepository>();
builder.Services.AddScoped<VideoCreationService>();
builder.Services.AddSingleton<AdminRepository>();   // local store over the settings file
builder.Services.AddSingleton<S3Storage>();
builder.Services.AddSingleton<PasswordHasher>();
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

// Translate "database not configured yet" into a clean 503 JSON instead of a 500,
// so the catalog endpoints fail gracefully until the dashboard configures the DB.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (DatabaseNotConfiguredException ex)
    {
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { error = ex.Message });
        }
    }
});

// Serve the admin dashboard's static assets from wwwroot.
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

// Ensure the schema exists at startup IF a database is already configured (additive,
// idempotent). With zero config the app still boots — the dashboard drives setup and
// the schema is ensured lazily on first use. Never crash the process here.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<DatabaseService>();
        if (await db.TryEnsureSchemaAsync())
            app.Logger.LogInformation("Database schema ensured at startup.");
        else
            app.Logger.LogInformation("Database not configured yet; configure it at /admin.");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Schema initialization failed; check database settings in /admin.");
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
