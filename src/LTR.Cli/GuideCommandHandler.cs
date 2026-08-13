using System.Globalization;
using LTR.Catalogue;
using LTR.Core.Content;
using LTR.Core.Sources;

namespace LTR.Cli;

/// <summary>
/// Imports a stored source's programme guide and reports what came of it.
/// </summary>
/// <remarks>
/// <para>
/// Works against a stored source rather than a panel address on the command line, because the guide
/// import needs more than credentials: it needs the source's channels to match against, and the probe
/// result that says whether the panel serves a guide at all.
/// </para>
/// <para>
/// The figure worth reading is how many channels the guide reached. A guide can download and parse
/// perfectly and still match nothing, and no other number reveals it.
/// </para>
/// </remarks>
internal sealed class GuideCommandHandler
{
    /// <summary>How many unmatched channels to name, so the reason for a poor match rate is visible.</summary>
    private const int UnmatchedSampleSize = 10;

    private readonly ICatalogueStore _catalogue;
    private readonly IGuideImportService _guide;
    private readonly TimeProvider _timeProvider;

    public GuideCommandHandler(
        ICatalogueStore catalogue,
        IGuideImportService guide,
        TimeProvider timeProvider)
    {
        _catalogue = catalogue;
        _guide = guide;
        _timeProvider = timeProvider;
    }

    public async Task<int> ImportAsync(int sourceId, bool force, CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        Console.WriteLine($"Importing the guide for '{source.Name}'.");

        var progress = new Progress<GuideImportStage>(stage => Console.WriteLine($"  {stage}..."));

        var result = force
            ? await _guide.ImportAsync(source, progress, cancellationToken).ConfigureAwait(false)
            : await _guide.ImportIfStaleAsync(source, progress, cancellationToken).ConfigureAwait(false);

        switch (result.Outcome)
        {
            case GuideImportOutcome.NoGuideAvailable:
                Console.WriteLine(
                    "This source offers no guide. An Xtream panel without xmltv.php and a playlist that "
                    + "names no x-tvg-url both look like this, and neither is a fault.");
                return 0;

            case GuideImportOutcome.NotDue:
                Console.WriteLine(
                    $"The stored guide was imported at {ConsoleText.FormatUtc(source.LastGuideImportedUtc)} and is still "
                    + "fresh. Pass --force to fetch it anyway.");
                return 0;

            case GuideImportOutcome.Empty:
                Console.Error.WriteLine(
                    "The guide address answered but held no usable programme. It is probably not an XMLTV "
                    + "document.");
                return 1;
        }

        if (result.WasTruncated)
        {
            Console.WriteLine("The download ended mid-document; what was read before that was kept.");
        }

        await ReportAsync(source, result.Summary, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// Reports the stored guide without fetching anything, which is how a match rate is checked after the
    /// fact.
    /// </summary>
    public async Task<int> ShowAsync(int sourceId, CancellationToken cancellationToken)
    {
        if (await FindSourceAsync(sourceId, cancellationToken).ConfigureAwait(false) is not { } source)
        {
            return 1;
        }

        var summary = await _catalogue.GetGuideSummaryAsync(source.Id, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"Source     {source.Name}");
        Console.WriteLine($"Imported   {ConsoleText.FormatUtc(source.LastGuideImportedUtc)}");

        await ReportAsync(source, summary, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task ReportAsync(
        PlaylistSource source,
        GuideSummary? summary,
        CancellationToken cancellationToken)
    {
        if (summary is null)
        {
            return;
        }

        Console.WriteLine($"Guide      {summary.GuideChannelCount} channels, {summary.ProgrammeCount} programmes");
        Console.WriteLine($"Coverage   until {ConsoleText.FormatUtc(summary.CoverageUntilUtc)}");
        Console.WriteLine(
            $"Matched    {summary.MatchedChannelCount} of {summary.TotalChannelCount} channels "
            + $"({Percentage(summary.MatchedChannelCount, summary.TotalChannelCount)})");

        await ReportNowNextAsync(source.Id, cancellationToken).ConfigureAwait(false);
        await ReportUnmatchedAsync(source.Id, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Prints what is on right now on a handful of channels, which is the check that the times were read
    /// with the right offset. A guide two hours out looks correct in every count above.
    /// </summary>
    private async Task ReportNowNextAsync(int sourceId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var slices = await _catalogue.GetNowAndNextAsync(sourceId, now, cancellationToken).ConfigureAwait(false);
        var channels = await _catalogue.GetLiveChannelsAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var namesById = channels.ToDictionary(channel => channel.Id, channel => channel.Name);

        var running = slices.Where(slice => slice.Now is not null).Take(UnmatchedSampleSize).ToList();

        if (running.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine(
                "Nothing is on air according to the stored guide. If the counts above are non-zero, the "
                + "guide's coverage does not include the present moment.");
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"On air at {ConsoleText.FormatUtc(now)}:");

        foreach (var slice in running)
        {
            var name = namesById.TryGetValue(slice.ChannelId, out var channelName) ? channelName : "?";

            Console.WriteLine(
                $"  {ConsoleText.Truncate(name, 30),-30} {ConsoleText.FormatUtcTimeOfDay(slice.Now!.StartUtc)}-{ConsoleText.FormatUtcTimeOfDay(slice.Now.StopUtc)} "
                + $"{ConsoleText.Truncate(slice.Now.Title, 40)}");
        }
    }

    private async Task ReportUnmatchedAsync(int sourceId, CancellationToken cancellationToken)
    {
        var channels = await _catalogue.GetLiveChannelsAsync(sourceId, cancellationToken).ConfigureAwait(false);
        var unmatched = channels.Where(channel => channel.GuideChannelId is null).ToList();

        if (unmatched.Count == 0)
        {
            return;
        }

        Console.WriteLine();
        Console.WriteLine($"{unmatched.Count} channels found no guide entry, among them:");

        foreach (var channel in unmatched.Take(UnmatchedSampleSize))
        {
            var guideId = string.IsNullOrWhiteSpace(channel.EpgChannelId) ? "no guide id" : channel.EpgChannelId;
            Console.WriteLine($"  {ConsoleText.Truncate(channel.Name, 40),-40} ({guideId})");
        }
    }

    private async Task<PlaylistSource?> FindSourceAsync(int sourceId, CancellationToken cancellationToken)
    {
        var sources = await _catalogue.GetSourcesAsync(cancellationToken).ConfigureAwait(false);
        var source = sources.FirstOrDefault(candidate => candidate.Id == sourceId);

        if (source is null)
        {
            Console.Error.WriteLine($"No source with id {sourceId}. Run 'sources list' to see what there is.");
        }

        return source;
    }

    private static string Percentage(int part, int total)
    {
        return total == 0
            ? "n/a"
            : ((double)part / total).ToString("P0", CultureInfo.InvariantCulture);
    }
}
