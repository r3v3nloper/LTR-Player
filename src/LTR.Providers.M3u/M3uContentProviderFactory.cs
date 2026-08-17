using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.M3u;

/// <summary>
/// Creates <see cref="IContentProvider"/> instances for M3U sources.
/// </summary>
internal sealed class M3uContentProviderFactory : IContentProviderFactory
{
    private readonly M3uPlaylistLoader _loader;
    private readonly M3uUrlSanitizer _urlSanitizer;
    private readonly ILoggerFactory _loggerFactory;

    public M3uContentProviderFactory(
        M3uPlaylistLoader loader,
        M3uUrlSanitizer urlSanitizer,
        ILoggerFactory loggerFactory)
    {
        _loader = loader;
        _urlSanitizer = urlSanitizer;
        _loggerFactory = loggerFactory;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is M3uSource;
    }

    public IContentProvider Create(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not M3uSource m3uSource)
        {
            throw new NotSupportedException(
                $"{nameof(M3uContentProviderFactory)} handles M3U sources only, "
                + $"but got {source.GetType().Name}.");
        }

        return new M3uContentProvider(
            m3uSource,
            _loader,
            _urlSanitizer,
            _loggerFactory.CreateLogger<M3uContentProvider>());
    }
}
