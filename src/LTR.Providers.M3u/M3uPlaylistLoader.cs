using System.IO;
using LTR.Core.Sources;

namespace LTR.Providers.M3u;

/// <summary>
/// Fetches and parses the playlist behind an <see cref="M3uSource"/>.
/// </summary>
/// <remarks>
/// Handles both remote and local playlists, because users have both: a subscription URL, or a file
/// they were sent. <see cref="HttpClient"/> cannot open <c>file:</c> addresses, so that case is read
/// from disk directly rather than left to fail obscurely.
/// </remarks>
internal sealed class M3uPlaylistLoader
{
    private readonly HttpClient _httpClient;

    public M3uPlaylistLoader(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<M3uPlaylist> LoadAsync(M3uSource source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.PlaylistUrl.IsFile)
        {
            return await LoadFromFileAsync(source.PlaylistUrl, cancellationToken).ConfigureAwait(false);
        }

        return await LoadFromHttpAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<M3uPlaylist> LoadFromFileAsync(Uri playlistUrl, CancellationToken cancellationToken)
    {
        var path = playlistUrl.LocalPath;

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The playlist file does not exist.", path);
        }

        // detectEncodingFromByteOrderMarks, so a playlist saved as UTF-8 with a BOM or as UTF-16 reads
        // correctly instead of turning every non-ASCII channel name into noise.
        using var reader = new StreamReader(path, detectEncodingFromByteOrderMarks: true);
        return await M3uPlusParser.ParseAsync(reader, cancellationToken).ConfigureAwait(false);
    }

    private async Task<M3uPlaylist> LoadFromHttpAsync(M3uSource source, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, source.PlaylistUrl);

        if (!request.Headers.UserAgent.TryParseAdd(source.UserAgent))
        {
            request.Headers.TryAddWithoutValidation("User-Agent", source.UserAgent);
        }

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // Streamed rather than buffered: a full subscription playlist runs to several megabytes and
        // there is no reason to hold the text as well as the parsed entries.
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        return await M3uPlusParser.ParseAsync(reader, cancellationToken).ConfigureAwait(false);
    }
}
