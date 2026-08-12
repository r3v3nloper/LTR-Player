using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Shows an element while a flag is <see langword="false"/>.
/// </summary>
/// <remarks>
/// Used to swap the setup form for the channel list off a single <c>HasSource</c> flag, rather than
/// carrying a second property that exists only to be the negation of the first.
/// </remarks>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is true ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Visibility is presentation state and is never written back.");
    }
}
