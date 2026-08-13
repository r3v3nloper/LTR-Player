using LTR.Core.Sources;

namespace LTR.Providers;

/// <summary>
/// Selects provider components by asking each whether it handles the source.
/// </summary>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IEnumerable<IContentProviderFactory> _factories;
    private readonly IEnumerable<IProviderCapabilityProbe> _capabilityProbes;
    private readonly IEnumerable<IStreamUrlResolver> _streamUrlResolvers;

    public ProviderRegistry(
        IEnumerable<IContentProviderFactory> factories,
        IEnumerable<IProviderCapabilityProbe> capabilityProbes,
        IEnumerable<IStreamUrlResolver> streamUrlResolvers)
    {
        _factories = factories;
        _capabilityProbes = capabilityProbes;
        _streamUrlResolvers = streamUrlResolvers;
    }

    public IContentProvider CreateProvider(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Select(_factories, source, factory => factory.Supports(source), "content provider").Create(source);
    }

    public IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Select(_capabilityProbes, source, probe => probe.Supports(source), "capability probe");
    }

    public IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return Select(_streamUrlResolvers, source, resolver => resolver.Supports(source), "stream url resolver");
    }

    /// <summary>
    /// Returns the first candidate that accepts the source, and explains itself when none does.
    /// </summary>
    /// <remarks>
    /// The message names both the missing component and the source type, because the realistic cause is
    /// a provider package that was not registered — a diagnosis the caller cannot make from a bare
    /// "not supported".
    /// </remarks>
    private static T Select<T>(
        IEnumerable<T> candidates,
        PlaylistSource source,
        Func<T, bool> accepts,
        string componentName)
    {
        foreach (var candidate in candidates)
        {
            if (accepts(candidate))
            {
                return candidate;
            }
        }

        throw new NotSupportedException(
            $"No {componentName} is registered for {source.GetType().Name}. The provider package for "
            + "this source type is probably missing from the service registration.");
    }
}
