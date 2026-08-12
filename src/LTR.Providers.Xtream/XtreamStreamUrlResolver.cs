using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Providers.Xtream;

/// <summary>
/// Builds playable addresses for channels belonging to an Xtream source.
/// </summary>
internal sealed class XtreamStreamUrlResolver : IStreamUrlResolver
{
    public bool Supports(PlaylistSource source)
    {
        return source is XtreamSource;
    }

    public MediaRequest ResolveLive(PlaylistSource source, Channel channel)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(channel);

        if (source is not XtreamSource xtreamSource)
        {
            throw new NotSupportedException(
                $"{nameof(XtreamStreamUrlResolver)} handles Xtream sources only, but got {source.GetType().Name}.");
        }

        var format = ChooseStreamFormat(xtreamSource);
        var url = XtreamEndpoints.LiveStream(
            xtreamSource,
            channel.ExternalId,
            format,
            xtreamSource.Capabilities.RequiresLivePathSegment);

        return new MediaRequest(url, xtreamSource.UserAgent, format, channel.Name);
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
