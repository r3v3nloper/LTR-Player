using System.Globalization;
using LTR.Core.Content;

namespace LTR.Player.Wpf;

/// <summary>
/// One row of the series list, before its seasons have been fetched.
/// </summary>
public sealed class SeriesItemViewModel
{
    public SeriesItemViewModel(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        Series = series;
    }

    public Series Series { get; }

    public int Id => Series.Id;

    public string Name => Series.Name;

    public string? CoverUrl => Series.CoverUrl;

    public string Details => string.Join(" · ", DetailParts());

    private IEnumerable<string> DetailParts()
    {
        if (Series.Year is { } year)
        {
            yield return year.ToString(CultureInfo.CurrentCulture);
        }

        if (Series.Rating is { } rating and > 0)
        {
            yield return rating.ToString("0.#", CultureInfo.CurrentCulture);
        }

        if (!string.IsNullOrWhiteSpace(Series.Genre))
        {
            yield return Series.Genre;
        }
    }
}
