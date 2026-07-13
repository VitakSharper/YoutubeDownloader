using System;
using System.Globalization;
using Avalonia.Data.Converters;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Avalonia.Converters;

/// <summary>Pretty label for the format combo: AudioMp3 -> "MP3 audio", Video -> "Video (MP4)".</summary>
public sealed class DownloadModeDisplayConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DownloadMode.AudioMp3 => "MP3 audio",
        DownloadMode.AudioOpus => "Opus audio",
        DownloadMode.Video => "Video (MP4)",
        _ => value?.ToString() ?? ""
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
