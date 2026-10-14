using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>Maps an output extension to the save-dialog filter shown for it.</summary>
public static class SaveFileTypes
{
    public static SaveFileType For(string extension)
    {
        var ext = extension.Trim().TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "mp3" => new SaveFileType("MP3 audio", "*.mp3"),
            "opus" => new SaveFileType("Opus audio", "*.opus"),
            "mp4" => new SaveFileType("MP4 video", "*.mp4"),
            _ => new SaveFileType($"{ext.ToUpperInvariant()} file", $"*.{ext}")
        };
    }
}
