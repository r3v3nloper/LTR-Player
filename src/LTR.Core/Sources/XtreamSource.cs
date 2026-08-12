namespace LTR.Core.Sources;

/// <summary>
/// A subscription reached through the Xtream Codes player API.
/// </summary>
public sealed class XtreamSource : PlaylistSource
{
    /// <summary>
    /// Scheme, host and port of the panel, without a trailing path. Action URLs are appended.
    /// </summary>
    public required Uri BaseUrl { get; set; }

    public required string Username { get; set; }

    /// <summary>
    /// Held in the representation produced by <see cref="ICredentialProtector"/>; the persistence
    /// layer converts to and from plaintext, so consumers of this property see plaintext.
    /// </summary>
    public required string Password { get; set; }

    public override Uri Endpoint => BaseUrl;
}
