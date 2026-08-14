using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Builds playable addresses for channels belonging to an Xtream source.
/// </summary>
internal sealed class XtreamStreamUrlResolver : IStreamUrlResolver
{
    /// <summary>
    /// Container assumed for a film or episode whose own is unknown.
    /// </summary>
    /// <remarks>
    /// The same reasoning as the live path segment: an address cannot be probed without occupying one of
    /// the account's very few connections, so the overwhelmingly common case is assumed and a 404 is what
    /// corrects it. Nearly every panel stores films as <c>mp4</c>, and a listing that omits the extension
    /// usually states it in the detail call the film's page makes anyway.
    /// </remarks>
    internal const string DefaultContainerExtension = "mp4";

    public bool Supports(PlaylistSource source)
    {
        return source is XtreamSource;
    }

    public MediaRequest ResolveLive(PlaylistSource source, Channel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var xtreamSource = RequireXtream(source);
        var format = ChooseStreamFormat(xtreamSource);
        var url = XtreamEndpoints.LiveStream(
            xtreamSource,
            channel.ExternalId,
            format,
            xtreamSource.Capabilities.RequiresLivePathSegment);

        return new MediaRequest(url, xtreamSource.UserAgent, format, channel.Name);
    }

    public MediaRequest ResolveMovie(PlaylistSource source, VodItem movie, TimeSpan? startAt = null)
    {
        ArgumentNullException.ThrowIfNull(movie);

        var xtreamSource = RequireXtream(source);
        var url = XtreamEndpoints.MovieStream(
            xtreamSource,
            movie.ExternalId,
            ContainerFor(movie.ContainerExtension));

        return new MediaRequest(
            url,
            xtreamSource.UserAgent,
            StreamFormat.ProgressiveFile,
            movie.Name,
            startAt);
    }

    public MediaRequest ResolveEpisode(PlaylistSource source, Episode episode, TimeSpan? startAt = null)
    {
        ArgumentNullException.ThrowIfNull(episode);

        var xtreamSource = RequireXtream(source);
        var url = XtreamEndpoints.EpisodeStream(
            xtreamSource,
            episode.ExternalId,
            ContainerFor(episode.ContainerExtension));

        return new MediaRequest(
            url,
            xtreamSource.UserAgent,
            StreamFormat.ProgressiveFile,
            episode.Title,
            startAt);
    }

    private static XtreamSource RequireXtream(PlaylistSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source is not XtreamSource xtreamSource)
        {
            throw new NotSupportedException(
                $"{nameof(XtreamStreamUrlResolver)} handles Xtream sources only, but got {source.GetType().Name}.");
        }

        return xtreamSource;
    }

    private static string ContainerFor(string? containerExtension)
    {
        return string.IsNullOrWhiteSpace(containerExtension)
            ? DefaultContainerExtension
            : containerExtension.Trim();
    }

    /// <summary>
    /// Picks the container to request: the user's preference when the panel serves it, otherwise
    /// whatever the panel does serve.
    /// </summary>
    /// <remarks>
    /// MPEG-TS is preferred over HLS as the fallback because it zaps noticeably faster; HLS is only
    /// chosen when the panel does not offer transport streams at all. An unprobed source keeps the
    /// user's preference rather than guessing.
    /// </remarks>
    internal static StreamFormat ChooseStreamFormat(XtreamSource source)
    {
        var capabilities = source.Capabilities;

        if (!capabilities.HasBeenProbed)
        {
            return source.PreferredStreamFormat;
        }

        var preferredIsAvailable = source.PreferredStreamFormat switch
        {
            StreamFormat.MpegTs => capabilities.SupportsMpegTs,
            StreamFormat.HlsPlaylist => capabilities.SupportsHls,
            _ => false,
        };

        if (preferredIsAvailable)
        {
            return source.PreferredStreamFormat;
        }

        if (capabilities.SupportsMpegTs)
        {
            return StreamFormat.MpegTs;
        }

        if (capabilities.SupportsHls)
        {
            return StreamFormat.HlsPlaylist;
        }

        // The panel reported no format this player understands. Try the preference anyway rather
        // than refusing outright, since panels routinely under-report allowed_output_formats.
        return source.PreferredStreamFormat;
    }
}
