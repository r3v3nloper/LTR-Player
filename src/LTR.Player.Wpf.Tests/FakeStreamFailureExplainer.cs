using LTR.Catalogue;
using LTR.Core.Playback;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Answers with whatever a test says the provider would.
/// </summary>
internal sealed class FakeStreamFailureExplainer : IStreamFailureExplainer
{
    /// <summary>What to report. Channel-unavailable by default, which is the ordinary case.</summary>
    public StreamFailureReason Reason { get; set; } = StreamFailureReason.ChannelUnavailable;

    /// <summary>The sources it was asked about, so a test can prove the panel was not asked needlessly.</summary>
    public List<PlaylistSource> Asked { get; } = [];

    public Task<StreamFailureReason> ExplainAsync(
        PlaylistSource source,
        CancellationToken cancellationToken)
    {
        Asked.Add(source);

        return Task.FromResult(Reason);
    }
}
