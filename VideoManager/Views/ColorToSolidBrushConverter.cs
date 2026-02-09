using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace VideoManager.Views;

/// <summary>
/// Converts a hex color string (e.g. "#FF5722") to a SolidColorBrush.
/// Falls back to the default theme primary color when the value is null or invalid.
/// </summary>
public class ColorToSolidBrushConverter : IValueConverter
{
    /// <summary>
    /// Default fallback color used when Tag.Color is null or invalid.
    /// Material Design DeepPurple 300 — matches the app's primary theme.
    /// </summary>
    private static readonly Color DefaultColor = (Color)ColorConverter.ConvertFromString("#9575CD");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hex);
                return new SolidColorBrush(color);
            }
            catch
            {
                // Invalid hex string — fall through to default
            }
        }

        return new SolidColorBrush(DefaultColor);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts a hex color string to a contrasting foreground color (white or black)
/// for readable text on colored backgrounds.
/// </summary>
public class ColorToForegroundConverter : IValueConverter
{
    private static readonly Color DefaultColor = (Color)ColorConverter.ConvertFromString("#9575CD");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        Color bgColor = DefaultColor;

        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                bgColor = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch
            {
                // Invalid hex — use default
            }
        }

        // Calculate relative luminance using sRGB coefficients
        double luminance = 0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B;
        var foreground = luminance > 150 ? Colors.Black : Colors.White;
        return new SolidColorBrush(foreground);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
