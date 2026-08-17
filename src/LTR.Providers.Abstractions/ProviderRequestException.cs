namespace LTR.Providers;

/// <summary>
/// A provider answered a request in a way that cannot be used, and the address is part of the diagnosis.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="HttpRequestException"/> on purpose: a host that cannot be reached is a
/// different problem for the user than a provider that answered — with an HTML maintenance page at HTTP
/// 200, or a 404 for a document it does not serve — and the front ends word them differently.
/// </para>
/// <para>
/// Protocol-neutral so that a caller reporting a failure does not need to know which protocol produced it.
/// Before this existed the CLI's error handling named <c>XtreamApiException</c> specifically, which is why
/// a playlist's failures reached the user with no address at all.
/// </para>
/// </remarks>
public class ProviderRequestException : Exception
{
    public ProviderRequestException(string message)
        : base(message)
    {
    }

    public ProviderRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// The address that produced the response, with credentials removed by that protocol's sanitiser.
    /// </summary>
    /// <remarks>
    /// Nothing may assign this from a raw address: it is written to console output and log files, which is
    /// what <see cref="ISensitiveUrlSanitizer"/> exists for.
    /// </remarks>
    public string? SanitizedUrl { get; init; }
}
