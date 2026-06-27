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
using Microsoft.AspNetCore.StaticFiles;

var builder = WebApplication.CreateBuilder(args);

// ---- Options (OPTIONAL env/appsettings seeds for the DB connection only) --------
// Only the database connection can be seeded from env; admin/storage/api-key live in
// the DB and are configured via the dashboard.
builder.Services.Configure<CloudflareOptions>(
    builder.Configuration.GetSection(CloudflareOptions.SectionName));
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection(DatabaseOptions.SectionName));
builder.Services.Configure<SettingsOptions>(
    builder.Configuration.GetSection(SettingsOptions.SectionName));
// APK self-update metadata (the APK is hosted on GitHub Releases, not here).
builder.Services.Configure<AppReleaseOptions>(
    builder.Configuration.GetSection(AppReleaseOptions.SectionName));

// ---- JSON: camelCase, ignore nulls, enums as strings --------------------
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ---- Config providers ----------------------------------------------------
// DbConfigStore persists ONLY the database connection to a local JSON file
// (App_Data/db.json). SettingsProvider exposes that effective DB config (live).
// AppConfigProvider reads storage config + the release API key FROM THE DATABASE
// (cached; reloaded on dashboard save), since those now live in the DB.
builder.Services.AddSingleton<DbConfigStore>();
builder.Services.AddSingleton<SettingsProvider>();
builder.Services.AddSingleton<AppConfigProvider>();

// ---- Data + storage ------------------------------------------------------
// Pluggable database: D1 (HTTP) or EF Core SQL (sqlite/postgres/mysql/sqlserver),
// chosen at runtime from the DB config. DatabaseService resolves the right impl per
// scope; the VideoRepository/AppReleaseRepository/AdminRepository facades ensure
// schema + delegate.
builder.Services.AddHttpClient<D1Client>();
builder.Services.AddSingleton<EfContextFactory>();
builder.Services.AddScoped<DatabaseService>();
builder.Services.AddScoped<VideoRepository>();
builder.Services.AddScoped<AppReleaseRepository>();
builder.Services.AddScoped<AdminRepository>();      // DB-backed (admin lives in the DB)
builder.Services.AddScoped<VideoCreationService>();
// In-memory server→storage upload progress, polled by the admin dashboard.
builder.Services.AddSingleton<UploadProgressTracker>();
// Storage providers: S3 (default) + Local filesystem, both singletons. StorageRouter
// picks the active one PER CALL from the DB config, so the dashboard can switch at
// runtime. Endpoints inject StorageRouter (as IObjectStorage / StorageRouter).
builder.Services.AddSingleton<S3Storage>();
builder.Services.AddSingleton<LocalStorage>();
builder.Services.AddSingleton<StorageRouter>();
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

// Large uploads (videos / apks) via multipart. TWO independent limits must both
// allow the upload, or the request is rejected:
//   1. Kestrel's MaxRequestBodySize — the raw request body cap. Defaults to only
//      ~28.6 MB (30,000,000 bytes); without raising it large videos fail with
//      "Request body too large".
//   2. FormOptions.MultipartBodyLengthLimit — the multipart form-body cap.
// Keep them in sync so the binding limit is a predictable 2 GB.
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 2L * 1024 * 1024 * 1024; // 2 GB
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; // 2 GB
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
// IMPORTANT: this must run BEFORE routing. The SPA fallback below
// (/admin/{**rest}) is a catch-all that also matches /admin/app.js and
// /admin/styles.css. In a WebApplication, if UseRouting() is not called
// explicitly it is auto-inserted at the very start of the pipeline — so that
// catch-all endpoint gets matched before UseStaticFiles runs, and
// StaticFileMiddleware then defers to the matched endpoint, returning
// index.html for every asset (breaking the admin page entirely). Calling
// UseStaticFiles() and only THEN UseRouting() lets real files win; just
// genuine client-side deep links fall through to the SPA fallback.
// Content types for static files. The bundled APK (wwwroot/app/app-release.apk) is served
// directly from here — make sure `.apk` maps to the Android package MIME type (it is NOT in
// the framework default set, so without this the static-file middleware would 404 it).
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".apk"] = "application/vnd.android.package-archive";

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes,
    // The admin dashboard is an SPA: app.js/styles.css/index.html change with every
    // backend upgrade. Without a Cache-Control directive browsers fall back to
    // *heuristic* caching and can serve a stale asset after an upgrade (this is exactly
    // why a fixed page can still look broken until a hard refresh). Force revalidation
    // for /admin assets — the ETag/Last-Modified still let the server answer 304, so
    // it's cheap, but the browser always checks. Non-/admin paths keep default caching.
    //
    // The APK lives at a FIXED url (/app/app-release.apk) but its bytes change every release,
    // so it gets the same no-cache treatment — otherwise a CDN/browser could hand back the
    // previous build. (The app also verifies sha256, so a stale download would fail closed.)
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path;
        if (path.StartsWithSegments("/admin") || path.StartsWithSegments("/app"))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    }
});
app.UseRouting();

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

// Health + identity signature so a client can verify this is genuinely THIS backend
// (not a random URL that returns 200). Additive: `status` stays; `service`/`api` are new.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", service = "tv-video-hub", api = "v1" }))
    .WithTags("system");

app.MapVideoEndpoints();
app.MapAppEndpoints();
app.MapAdminEndpoints();
app.MapMediaEndpoints();

// Serve the admin dashboard SPA at /admin (and /admin/* deep links). This does
// not intercept /api/* routes. Real assets (app.js, styles.css, …) are served by
// UseStaticFiles above; this catch-all only handles extension-less client-side
// routes. A request for a missing *.js/*.css must 404 rather than return HTML,
// otherwise a typo'd/renamed asset silently returns index.html and the browser
// fails to parse HTML as JavaScript — the exact failure this fallback used to cause.
app.MapGet("/admin", (HttpContext http) => ServeAdminIndex(http, app.Environment)).ExcludeFromDescription();
app.MapGet("/admin/{**rest}", (HttpContext http, string rest) =>
    Path.HasExtension(rest) ? Results.NotFound() : ServeAdminIndex(http, app.Environment))
    .ExcludeFromDescription();

app.Run();

static IResult ServeAdminIndex(HttpContext http, IWebHostEnvironment env)
{
    // Always revalidate the SPA shell so an upgraded dashboard is never masked by a
    // stale cached index.html (matches the /admin asset policy in UseStaticFiles).
    http.Response.Headers.CacheControl = "no-cache";
    var path = Path.Combine(env.WebRootPath ?? Path.Combine(env.ContentRootPath, "wwwroot"),
        "admin", "index.html");
    return File.Exists(path)
        ? Results.File(path, "text/html")
        : Results.NotFound();
}

// Exposed for integration testing with WebApplicationFactory.
public partial class Program;
