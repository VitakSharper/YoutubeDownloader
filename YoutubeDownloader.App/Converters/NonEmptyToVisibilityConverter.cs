using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace YoutubeDownloader.App.Converters;

/// <summary>Visible when the bound string is non-empty; otherwise Collapsed.</summary>
public sealed class NonEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
