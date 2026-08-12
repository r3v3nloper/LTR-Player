namespace LTR.Providers.Xtream;

/// <summary>
/// A panel responded in a way that cannot be interpreted as a player API result.
/// </summary>
/// <remarks>
/// Distinct from <see cref="HttpRequestException"/> on purpose: a panel returning HTTP 200 with an
/// HTML maintenance page is a different problem for the user than the host being unreachable, and
/// the UI reports them differently.
/// </remarks>
public sealed class XtreamApiException : Exception
{
    public XtreamApiException(string message)
        : base(message)
    {
    }

    public XtreamApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>The address that produced the response, with credentials removed.</summary>
    public string? SanitizedUrl { get; init; }
}
