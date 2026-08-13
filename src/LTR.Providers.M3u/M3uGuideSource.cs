using System.IO;
using LTR.Core.Sources;
using Microsoft.Extensions.Logging;

namespace LTR.Providers.M3u;

/// <summary>
/// Downloads the XMLTV guide an M3U source points at.
/// </summary>
/// <remarks>
/// A playlist carries no programme data of its own, so the guide is always somewhere else: either an
/// address the user supplied, or the <c>x-tvg-url</c> the playlist declares on its header line. Both
/// forms occur, and a subscription playlist that names its own guide is the common one.
/// </remarks>
internal sealed class M3uGuideSource : IGuideSource
{
    private readonly HttpClient _httpClient;
    private readonly M3uPlaylistLoader _loader;
    private readonly ILogger<M3uGuideSource> _logger;

    public M3uGuideSource(HttpClient httpClient, M3uPlaylistLoader loader, ILogger<M3uGuideSource> logger)
    {
        _httpClient = httpClient;
        _loader = loader;
        _logger = logger;
    }

    public bool Supports(PlaylistSource source)
    {
        return source is M3uSource;
    }

    public async Task<bool> TryReadGuideAsync(
        PlaylistSource source,
        Func<Stream, CancellationToken, Task> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(read);

        if (source is not M3uSource m3uSource)
        {
            throw new NotSupportedException(
                $"{nameof(M3uGuideSource)} handles M3U sources only, but got {source.GetType().Name}.");
        }

        if (await ResolveGuideUrlAsync(m3uSource, cancellationToken).ConfigureAwait(false) is not { } guideUrl)
        {
            M3uLog.NoGuideDeclared(_logger, m3uSource.Name);
            return false;
        }

        if (guideUrl.IsFile)
        {
            var file = File.OpenRead(guideUrl.LocalPath);
            await using (file.ConfigureAwait(false))
            {
                await read(file, cancellationToken).ConfigureAwait(false);
            }

            return true;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, guideUrl);

        if (!request.Headers.UserAgent.TryParseAdd(m3uSource.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", m3uSource.UserAgent);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            await read(stream, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// Prefers the address configured on the source, and otherwise asks the playlist.
    /// </summary>
    /// <remarks>
    /// Reading the playlist to find out costs nothing after an import, because the loader caches the
    /// parsed document — and where it is not cached, downloading the playlist to learn where its guide
    /// lives is the only way to find out at all.
    /// </remarks>
    private async Task<Uri?> ResolveGuideUrlAsync(M3uSource source, CancellationToken cancellationToken)
    {
        if (source.EpgUrl is { } configured)
        {
            return configured;
        }

        var playlist = await _loader.LoadAsync(source, cancellationToken).ConfigureAwait(false);
        return playlist.EpgUrl;
    }
}
