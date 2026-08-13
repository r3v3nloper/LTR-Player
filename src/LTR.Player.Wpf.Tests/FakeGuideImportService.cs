using LTR.Catalogue;
using LTR.Core.Sources;

namespace LTR.Player.Wpf;

/// <summary>
/// Stands in for the guide import, and records which of the two entry points was used.
/// </summary>
/// <remarks>
/// The distinction matters: the button must fetch unconditionally and the automatic path must not, and
/// nothing else in the shell can tell the two apart.
/// </remarks>
internal sealed class FakeGuideImportService : IGuideImportService
{
    private readonly TaskCompletionSource _release = new();

    public List<PlaylistSource> Imported { get; } = [];

    public List<PlaylistSource> ImportedIfStale { get; } = [];

    public GuideImportResult Result { get; set; } =
        new(GuideImportOutcome.Imported, ProgrammeCount: 12, MatchedChannelCount: 3, WasTruncated: false, Summary: null);

    /// <summary>
    /// When set, an import blocks until <see cref="Release"/> is called, so a test can observe the state
    /// while one is in flight.
    /// </summary>
    public bool BlockUntilReleased { get; set; }

    /// <summary>
    /// Whether to report a stage. Off by default, and deliberately so: <see cref="Progress{T}"/> delivers
    /// through a synchronisation context, and a test with none can see the stage message land after the
    /// result message it is asserting on. Only the test that cares about progress turns it on.
    /// </summary>
    public bool ReportProgress { get; set; }

    public Task<GuideImportResult> ImportAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken)
    {
        Imported.Add(source);
        return RunAsync(progress, cancellationToken);
    }

    public Task<GuideImportResult> ImportIfStaleAsync(
        PlaylistSource source,
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken)
    {
        ImportedIfStale.Add(source);
        return RunAsync(progress, cancellationToken);
    }

    public void Release()
    {
        _release.TrySetResult();
    }

    private async Task<GuideImportResult> RunAsync(
        IProgress<GuideImportStage>? progress,
        CancellationToken cancellationToken)
    {
        if (ReportProgress)
        {
            progress?.Report(GuideImportStage.Reading);
        }

        if (BlockUntilReleased)
        {
            await _release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result;
    }
}
