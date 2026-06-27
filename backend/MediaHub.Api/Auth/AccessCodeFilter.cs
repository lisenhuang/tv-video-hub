using MediaHub.Api.Settings;

namespace MediaHub.Api.Auth;

/// <summary>
/// Endpoint filter that gates content (the catalog + detail endpoints) behind an access code
/// when the admin has enabled it. The code is sent in the <c>X-Access-Code</c> header and
/// validated case-insensitively against the codes in <see cref="AppConfigProvider"/>.
///
/// BACKWARD COMPATIBILITY: when the gate is DISABLED (the default) this filter is a no-op, so
/// reads stay public exactly as before and every already-installed app keeps working. Only when
/// an admin turns the gate ON does a missing/invalid code get a 403 — and the self-update path
/// (<c>/api/app/latest</c> + the APK) is never gated, so devices can always update into a
/// gate-aware build first.
/// </summary>
public sealed class AccessCodeFilter(AppConfigProvider appConfig) : IEndpointFilter
{
    public const string HeaderName = "X-Access-Code";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var ct = context.HttpContext.RequestAborted;

        // Gate off → public (unchanged behaviour for old + new apps).
        if (!await appConfig.GetAccessGateEnabledAsync(ct))
            return await next(context);

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (await appConfig.IsAccessCodeValidAsync(provided, ct))
            return await next(context);

        // Distinguishable body so the app knows to prompt for a code (vs a generic error).
        return Results.Json(
            new { error = "An access code is required.", code = "ACCESS_CODE_REQUIRED" },
            statusCode: StatusCodes.Status403Forbidden);
    }
}
