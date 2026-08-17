using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;
using LTR.TestSupport;

namespace LTR.Player.Wpf;

/// <summary>
/// Resolves stream addresses without a provider, since the view model only needs an address to hand to
/// playback.
/// </summary>
/// <remarks>
/// The only member it answers, and deliberately so: the window imports through the import service rather
/// than through the registry, and nothing in it logs or prints an address. Reaching any other member from a
/// view model would be a change of design, and the base class turns that into a failing test that says which
/// member was asked for.
/// </remarks>
internal sealed class StubProviderRegistry : NotSupportedProviderRegistry, IStreamUrlResolver
{
    public override IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source)
    {
        return this;
    }

    public bool Supports(PlaylistSource source)
    {
        return true;
    }

    public MediaRequest ResolveLive(PlaylistSource source, Channel channel)
    {
        return new MediaRequest(
            new Uri($"http://example.invalid/{channel.ExternalId}.ts"),
            source.UserAgent,
            StreamFormat.MpegTs,
            channel.Name);
    }

    public MediaRequest ResolveMovie(PlaylistSource source, VodItem movie, TimeSpan? startAt = null)
    {
        return new MediaRequest(
            new Uri($"http://example.invalid/movie/{movie.ExternalId}.mp4"),
            source.UserAgent,
            StreamFormat.ProgressiveFile,
            movie.Name,
            startAt);
    }

    public MediaRequest ResolveEpisode(PlaylistSource source, Episode episode, TimeSpan? startAt = null)
    {
        return new MediaRequest(
            new Uri($"http://example.invalid/series/{episode.ExternalId}.mkv"),
            source.UserAgent,
            StreamFormat.ProgressiveFile,
            episode.Title,
            startAt);
    }
}
