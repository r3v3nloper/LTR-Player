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

    /// <summary>
    /// Year, rating and genre on one line. No running time, unlike a film: a series has none of its own.
    /// </summary>
    public string Details => CatalogueDetailLine.Build(Series.Year, Series.Rating, Series.Genre);
}
