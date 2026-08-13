using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Compares a bound enum value against the one named in the converter parameter.
/// </summary>
/// <remarks>
/// <para>
/// Serves both radio buttons and visibility, returning whichever of <see cref="bool"/> or
/// <see cref="Visibility"/> the binding target asked for. WPF cannot chain converters, so the
/// alternative is a second near-identical converter or a redundant boolean property per enum value on
/// the view model.
/// </para>
/// <para>
/// <see cref="ConvertBack"/> is implemented, because a radio button has to be able to set the value.
/// </para>
/// </remarks>
public sealed class EnumMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var matches = value is not null
            && parameter is not null
            && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

        if (targetType == typeof(Visibility))
        {
            return matches ? Visibility.Visible : Visibility.Collapsed;
        }

        return matches;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Only the button being switched on carries information; the one switching off is followed by
        // its partner switching on, and acting on both would fight the binding.
        if (value is not true || parameter is null)
        {
            return Binding.DoNothing;
        }

        return Enum.TryParse(targetType, parameter.ToString(), ignoreCase: false, out var parsed)
            ? parsed
            : Binding.DoNothing;
    }
}
