using MediaHub.Api.Settings;

namespace MediaHub.Api.Auth;

/// <summary>
/// Endpoint filter that guards write endpoints with the <c>X-Api-Key</c> header.
/// The key now lives IN THE DATABASE and is read live (cached) from
/// <see cref="AppConfigProvider"/>, so dashboard changes take effect without a restart.
/// If no key is configured (or the DB isn't ready yet) the endpoint is refused (503)
/// rather than left open.
/// </summary>
public sealed class ApiKeyFilter(AppConfigProvider appConfig) : IEndpointFilter
{
    public const string HeaderName = "X-Api-Key";

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var key = await appConfig.GetApiKeyAsync(context.HttpContext.RequestAborted);
        if (string.IsNullOrEmpty(key))
        {
            return Results.Problem(
                "Write endpoints are disabled: no release API key is configured.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (!CryptographicEquals(provided, key))
            return Results.Unauthorized();

        return await next(context);
    }

    // Constant-time comparison to avoid leaking the key via timing.
    private static bool CryptographicEquals(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var diff = 0;
        for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
