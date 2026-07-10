using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Returns true when the bound string is non-empty (for IsVisible bindings).</summary>
public sealed class StringNotEmptyToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
