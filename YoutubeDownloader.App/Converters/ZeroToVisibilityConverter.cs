using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YoutubeDownloader.App.Converters;

/// <summary>Visible when the bound integer (e.g. a collection Count) is 0; otherwise Collapsed.</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is int n && n == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
