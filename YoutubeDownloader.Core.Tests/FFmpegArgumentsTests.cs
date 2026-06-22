using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegArgumentsTests
{
    [Fact]
    public void ForMp3_BuildsLameArgsWithBitrateAndQuotedPaths()
    {
        var args = FFmpegArguments.ForMp3(@"C:\in.webm", @"C:\out.mp3", 192);

        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("\"C:\\out.mp3\"", args);
        Assert.StartsWith("-y", args);
    }

    [Fact]
    public void ForMuxMp4_CopiesVideoAndEncodesAacWithFaststart()
    {
        var args = FFmpegArguments.ForMuxMp4(@"C:\v.mp4", @"C:\a.m4a", @"C:\out.mp4");

        Assert.Contains("-i \"C:\\v.mp4\"", args);
        Assert.Contains("-i \"C:\\a.m4a\"", args);
        Assert.Contains("-c:v copy", args);
        Assert.Contains("-c:a aac", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.EndsWith("\"C:\\out.mp4\"", args);
    }
}
