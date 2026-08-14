using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One episode row within a selected season.
/// </summary>
public sealed class EpisodeItemViewModel
{
    public EpisodeItemViewModel(Episode episode, int seasonNumber)
    {
        ArgumentNullException.ThrowIfNull(episode);

        Episode = episode;
        Label = EpisodeNaming.Label(seasonNumber, episode.Number);
    }

    public Episode Episode { get; }

    public int Id => Episode.Id;

    /// <summary>The conventional short form, such as <c>S02E05</c>.</summary>
    public string Label { get; }

    public string Title => Episode.Title;

    public string? StillUrl => Episode.StillUrl;

    public string? Plot => Episode.Plot;

    public string Details =>
        Episode.Duration is { } duration ? DurationText.Format(duration) : string.Empty;

    public bool HasResumePoint => Episode.ResumePositionSeconds is > 0;

    public string ResumeLabel =>
        Episode.ResumePositionSeconds is { } seconds and > 0
            ? $"Resume at {DurationText.Format(TimeSpan.FromSeconds(seconds))}"
            : string.Empty;

    public bool IsWatched => Episode.IsWatched;
}
