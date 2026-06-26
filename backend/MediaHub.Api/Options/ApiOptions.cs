namespace MediaHub.Api.Options;

/// <summary>API-level settings, bound from the "Api" config section.</summary>
public sealed class ApiOptions
{
    public const string SectionName = "Api";

    /// <summary>
    /// Shared secret required (via the <c>X-Api-Key</c> header) for write
    /// endpoints. If left empty, write endpoints are rejected with 503 so the
    /// service never ships "open" by accident.
    /// </summary>
    public string Key { get; set; } = string.Empty;
}
