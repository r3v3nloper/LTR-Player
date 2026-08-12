using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Shows an element depending on whether a string has content.
/// </summary>
/// <remarks>
/// One configurable converter with two declared instances, rather than a matched pair of types whose
/// only difference is which way round the test runs.
/// </remarks>
public sealed class StringPresenceToVisibilityConverter : IValueConverter
{
    /// <summary>
    /// When <see langword="true"/>, the element is visible for a non-empty string; when
    /// <see langword="false"/>, it is visible for an empty one.
    /// </summary>
    public bool VisibleWhenPresent { get; set; } = true;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasContent = !string.IsNullOrWhiteSpace(value as string);
        return hasContent == VisibleWhenPresent ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Visibility is presentation state and is never written back.");
    }
}
