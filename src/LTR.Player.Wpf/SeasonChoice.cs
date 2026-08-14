using System.Globalization;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// An entry of the season picker.
/// </summary>
/// <remarks>
/// Carries the season itself rather than only its number, because selecting one has to produce its
/// episodes and looking them up again by number would mean holding the series in two places.
/// </remarks>
public sealed class SeasonChoice
{
    public SeasonChoice(Season season)
    {
        ArgumentNullException.ThrowIfNull(season);
        Season = season;
    }

    public Season Season { get; }

    public int Number => Season.Number;

    /// <summary>
    /// The provider's own season name where it gave one, and a plain "Season 2" otherwise.
    /// </summary>
    /// <remarks>
    /// Season zero is labelled as specials rather than as "Season 0", which is what providers file
    /// extras and behind-the-scenes material under.
    /// </remarks>
    public string Name
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Season.Name))
            {
                return Season.Name;
            }

            return Season.Number == 0
                ? "Specials"
                : string.Create(CultureInfo.CurrentCulture, $"Season {Season.Number}");
        }
    }

    public int EpisodeCount => Season.Episodes.Count;
}
