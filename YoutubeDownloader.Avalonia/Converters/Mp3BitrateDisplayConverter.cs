using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Pretty label for the bitrate combo: Kbps192 -> "192 kbps".</summary>
public sealed class Mp3BitrateDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Mp3Bitrate b ? $"{(int)b} kbps" : value?.ToString() ?? "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
