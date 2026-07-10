using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Returns true when the bound value is non-null (for IsVisible bindings).</summary>
public sealed class NullToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not null;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
