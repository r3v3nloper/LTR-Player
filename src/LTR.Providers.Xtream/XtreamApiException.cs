namespace LTR.Providers.Xtream;

/// <summary>
/// A panel responded in a way that cannot be interpreted as a player API result.
/// </summary>
/// <remarks>
/// Kept as its own type over <see cref="ProviderRequestException"/> because a caller that knows it is
/// talking to a panel can word the failure in those terms — the CLI says "Panel error" — while everything
/// that only knows it is talking to a provider catches the base and still gets the sanitised address.
/// </remarks>
public sealed class XtreamApiException : ProviderRequestException
{
    public XtreamApiException(string message)
        : base(message)
    {
    }

    public XtreamApiException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
