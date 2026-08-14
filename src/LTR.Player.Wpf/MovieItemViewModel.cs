using System.Globalization;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One row of the film list.
/// </summary>
/// <remarks>
/// Immutable, unlike <see cref="ChannelItemViewModel"/>: a film row has nothing that changes while it is
/// on screen, because the list is replaced whenever the search changes and progress is only ever recorded
/// for the one film that is playing. That is also why it needs no change notification.
/// </remarks>
public sealed class MovieItemViewModel
{
    public MovieItemViewModel(VodItem movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        Movie = movie;
    }

    public VodItem Movie { get; }

    public int Id => Movie.Id;

    public string Name => Movie.Name;

    public string? CoverUrl => Movie.CoverUrl;

    /// <summary>Year and running time on one line, with whatever of the two the provider stated.</summary>
    public string Details => string.Join(" · ", DetailParts());

    public bool HasResumePoint => Movie.ResumePositionSeconds is > 0;

    /// <summary>
    /// Where playback would pick up, as a label. Empty when the film was never started.
    /// </summary>
    public string ResumeLabel =>
        Movie.ResumePositionSeconds is { } seconds and > 0
            ? $"Resume at {DurationText.Format(TimeSpan.FromSeconds(seconds))}"
            : string.Empty;

    public bool IsWatched => Movie.IsWatched;

    /// <summary>What the play button says, which is the only place resuming is offered by name.</summary>
    public string PlayLabel => HasResumePoint ? ResumeLabel : "Play";

    private IEnumerable<string> DetailParts()
    {
        if (Movie.Year is { } year)
        {
            yield return year.ToString(CultureInfo.CurrentCulture);
        }

        if (Movie.Duration is { } duration)
        {
            yield return DurationText.Format(duration);
        }

        if (Movie.Rating is { } rating and > 0)
        {
            yield return rating.ToString("0.#", CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(Movie.Genre))
        {
            yield return Movie.Genre;
        }
    }
}
