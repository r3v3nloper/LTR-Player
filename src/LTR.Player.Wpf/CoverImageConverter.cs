using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace LTR.Player.Wpf;

/// <summary>
/// Turns a cover address into an image, decoded no larger than it is shown.
/// </summary>
/// <remarks>
/// <para>
/// Binding <c>Image.Source</c> to the address directly would work, and would also decode every poster at
/// its full size — a subscription's covers are commonly 1000 pixels tall, and a list of them held in
/// memory at that size is tens of megabytes for a few hundred rows. <see cref="DecodePixelWidth"/> caps
/// that at what the row actually displays.
/// </para>
/// <para>
/// The cache option is deliberately left at its default. Forcing a load would make each remote cover a
/// synchronous download on the UI thread, which freezes the list as it scrolls; the default fetches
/// asynchronously and fills the image in when it arrives.
/// </para>
/// </remarks>
public sealed class CoverImageConverter : IValueConverter
{
    /// <summary>Width to decode at, in pixels. Set per use, so a detail pane can ask for a larger one.</summary>
    public int DecodePixelWidth { get; set; } = 120;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string address
            || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var image = new BitmapImage();

        try
        {
            image.BeginInit();
            image.UriSource = uri;
            image.DecodePixelWidth = DecodePixelWidth;
            image.EndInit();
        }
        catch (Exception exception) when (exception is UriFormatException or NotSupportedException)
        {
            // A cover nobody can decode is not worth a broken row, let alone an exception dialog. Providers
            // do serve HTML error pages under image addresses.
            return null;
        }

        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Covers are displayed, never edited.");
    }
}
