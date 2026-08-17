using LTR.Core.Content;
using LTR.Core.Playback;
using LTR.Core.Sources;
using LTR.Providers;

namespace LTR.Player.Wpf;

/// <summary>
/// Resolves stream addresses without a provider, since the view model only needs an address to hand to
/// playback.
/// </summary>
internal sealed class StubProviderRegistry : IProviderRegistry, IStreamUrlResolver
{
    public IContentProvider CreateProvider(PlaylistSource source)
    {
        throw new NotSupportedException("The view model imports through the import service, not directly.");
    }

    public IProviderCapabilityProbe GetCapabilityProbe(PlaylistSource source)
    {
        throw new NotSupportedException("Capabilities are probed by the import service.");
    }

    public IStreamUrlResolver GetStreamUrlResolver(PlaylistSource source)
    {
        return this;
    }

    public IGuideSource GetGuideSource(PlaylistSource source)
    {
        throw new NotSupportedException("The view model imports the guide through the import service.");
    }

    public ISensitiveUrlSanitizer GetUrlSanitizer(PlaylistSource source)
    {
        throw new NotSupportedException("Nothing in the window logs or prints an address.");
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
