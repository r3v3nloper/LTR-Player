using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Shows an element depending on whether a reference is set.
/// </summary>
/// <remarks>
/// Used for the panel describing the selected programme, which has nothing to describe until something is
/// selected. Distinct from <see cref="StringPresenceToVisibilityConverter"/> because a bound object is not
/// a string and testing one for emptiness would always report present.
/// </remarks>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Visibility is presentation state and is never written back.");
    }
}
