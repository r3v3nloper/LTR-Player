using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Shows an element depending on whether a reference is set.
/// </summary>
/// <remarks>
/// Used for the panel describing the selected programme, which has nothing to describe until something is
/// selected, and for the two halves of the series section — one shown while nothing is open and the other
/// while something is. Distinct from <see cref="StringPresenceToVisibilityConverter"/> because a bound
/// object is not a string and testing one for emptiness would always report present.
/// </remarks>
public sealed class NullToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// Which way round the test runs. Two instances are registered rather than a second converter, for the
    /// same reason as the string one: the rule is identical and only its sense differs.
    /// </summary>
    public bool VisibleWhenPresent { get; set; } = true;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return (value is not null) == VisibleWhenPresent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Visibility is presentation state and is never written back.");
    }
}
