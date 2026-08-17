using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Finds the implementation that handles a given source.
/// </summary>
/// <remarks>
/// Every provider component comes in one variant per protocol, and picking the right one is the same
/// rule each time. Stating it once here keeps callers from injecting collections and re-deriving it —
/// and from silently receiving whichever implementation happened to be registered last, which is what
/// happens when a component is injected singly while several are registered.
/// </remarks>
public interface IProviderRegistry
{
    /// <summary>
    /// Creates a provider bound to <paramref name="source"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">No registered provider handles this source's protocol.</exception>
    IContentProvider CreateProvider(PlaylistSource source);

    /// <exception cref="NotSupportedException">No registered probe handles this source's protocol.</exception>
    IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source);

    /// <exception cref="NotSupportedException">No registered resolver handles this source's protocol.</exception>
    IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source);

    /// <exception cref="NotSupportedException">No registered guide source handles this source's protocol.</exception>
    IGuideSource GetGuideSource(PlaylistSource source);

    /// <summary>
    /// Returns the sanitiser for this source's protocol, for a caller about to log or print an address.
    /// </summary>
    /// <exception cref="NotSupportedException">No registered sanitiser handles this source's protocol.</exception>
    ISensitiveUrlSanitizer GetUrlSanitizer(PlaylistSource source);
}
