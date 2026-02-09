using System.Globalization;
using System.Windows.Data;

namespace VideoManager.Views;

/// <summary>
/// Converts a file size in bytes (long) to a human-readable string.
/// Examples: "1.5 GB", "320 MB", "45 KB".
/// </summary>
public class FileSizeFormatConverter : IValueConverter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB"];

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long bytes && bytes >= 0)
        {
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < Units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return unitIndex == 0
                ? $"{size:F0} {Units[unitIndex]}"
                : $"{size:F1} {Units[unitIndex]}";
        }

        return "0 B";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
