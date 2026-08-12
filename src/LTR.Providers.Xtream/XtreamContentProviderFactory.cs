using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.Xtream;

/// <summary>
/// Creates <see cref="IContentProvider"/> instances for Xtream sources.
/// </summary>
internal sealed class XtreamContentProviderFactory : IContentProviderFactory
{
    private readonly XtreamApiClient _client;
    private readonly TimeProvider _timeProvider;
    private readonly ILoggerFactory _loggerFactory;

    public XtreamContentProviderFactory(
        XtreamApiClient client,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory)
    {
        _client = client;
        _timeProvider = timeProvider;
        _loggerFactory = loggerFactory;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is XtreamSource;
    }

    public IContentProvider Create(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not XtreamSource xtreamSource)
        {
            throw new NotSupportedException(
                $"{nameof(XtreamContentProviderFactory)} handles Xtream sources only, "
                + $"but got {source.GetType().Name}.");
        }

        return new XtreamContentProvider(
            xtreamSource,
            _client,
            _timeProvider,
            _loggerFactory.CreateLogger<XtreamContentProvider>());
    }
}
