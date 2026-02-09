using System.Globalization;
using System.Windows.Data;
using VideoManager.Models;

namespace VideoManager.Views;

/// <summary>
/// Converts between SortField enum and ComboBox SelectedIndex (0=ImportedAt, 1=Duration, 2=FileSize).
/// </summary>
public class SortFieldToIndexConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SortField field)
        {
            return field switch
            {
                SortField.ImportedAt => 0,
                SortField.Duration => 1,
                SortField.FileSize => 2,
                _ => 0
            };
        }
        return 0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int index)
        {
            return index switch
            {
                0 => SortField.ImportedAt,
                1 => SortField.Duration,
                2 => SortField.FileSize,
                _ => SortField.ImportedAt
            };
        }
        return SortField.ImportedAt;
    }
}

/// <summary>
/// Converts SortDirection enum to a Material Design icon kind string for the sort direction button.
/// Ascending → SortAscending, Descending → SortDescending.
/// </summary>
public class SortDirectionToIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SortDirection direction)
        {
            return direction == SortDirection.Ascending
                ? MaterialDesignThemes.Wpf.PackIconKind.SortAscending
                : MaterialDesignThemes.Wpf.PackIconKind.SortDescending;
        }
        return MaterialDesignThemes.Wpf.PackIconKind.SortDescending;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// Converts SortDirection enum to a tooltip string for the sort direction button.
/// </summary>
public class SortDirectionToTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SortDirection direction)
        {
            return direction == SortDirection.Ascending ? "升序（点击切换为降序）" : "降序（点击切换为升序）";
        }
        return "排序方向";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
