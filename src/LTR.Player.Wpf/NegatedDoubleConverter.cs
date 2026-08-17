using System.Globalization;
using System.Windows.Data;

namespace LTR.Player.Wpf;

/// <summary>
/// Turns a scroll offset into the translation that follows it.
/// </summary>
/// <remarks>
/// The guide's channel names sit outside its horizontal scroller so that they cannot scroll away, which
/// leaves the programme blocks having to move by hand: they are shifted left by however far the timeline has
/// been scrolled right. One negation, in one place, rather than a mirrored offset property on the view model
/// that exists only because a transform counts the other way.
/// </remarks>
public sealed class NegatedDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double offset ? -offset : 0d;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // A scroll offset is moved by scrolling, never by the transform reporting back.
        return Binding.DoNothing;
    }
}
