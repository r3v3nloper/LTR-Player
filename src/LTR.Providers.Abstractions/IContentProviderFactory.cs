using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Creates the provider implementation matching a source's protocol.
/// </summary>
public interface IContentProviderFactory
{
    /// <summary>
    /// Whether this factory handles the given source's protocol.
    /// </summary>
    bool Supports(PlaylistSource source);

    /// <summary>
    /// Creates a provider bound to <paramref name="source"/>.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// The source's protocol is not handled by this factory.
    /// </exception>
    IContentProvider Create(PlaylistSource source);
}
