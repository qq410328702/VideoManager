using System.Globalization;
using System.Windows.Data;

namespace VideoManager.Views;

/// <summary>
/// Converts a <see cref="TimeSpan"/> to a human-readable duration string.
/// Examples: "01:23:45" for durations ≥ 1 hour, "23:45" otherwise.
/// </summary>
public class DurationFormatConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
        {
            return ts.TotalHours >= 1
                ? ts.ToString(@"hh\:mm\:ss")
                : ts.ToString(@"mm\:ss");
        }

        return "00:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
