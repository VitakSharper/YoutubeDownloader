using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Helpers;

public static class YouTubeUrlValidator
{
    public static bool IsValid(string? url) => GetVideoId(url) is not null;

    public static string? GetVideoId(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        var id = VideoId.TryParse(url);
        return id?.Value;
    }
}
