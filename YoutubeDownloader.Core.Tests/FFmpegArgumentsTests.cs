using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegArgumentsTests
{
    [Fact]
    public void ForMp3Cbr_BuildsLameCbrArgsWithBitrateAndQuotedPaths()
    {
        var args = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192);

        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("\"C:\\out.mp3\"", args);
        Assert.StartsWith("-y", args);
    }

    [Fact]
    public void ForMp3Vbr_BuildsLameVbrArgsWithQualityLevel()
    {
        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0);

        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a libmp3lame", args);
        Assert.Contains("-q:a 0", args);
        Assert.DoesNotContain("-b:a", args);
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
        Assert.Contains("-b:a 192k", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.EndsWith("\"C:\\out.mp4\"", args);
    }

    [Fact]
    public void ForMuxMp4CopyAudio_CopiesBothStreamsWithFaststart()
    {
        var args = FFmpegArguments.ForMuxMp4CopyAudio(@"C:\v.mp4", @"C:\a.mp4", @"C:\out.mp4");

        Assert.StartsWith("-y", args);
        Assert.Contains("-i \"C:\\v.mp4\"", args);
        Assert.Contains("-i \"C:\\a.mp4\"", args);
        Assert.Contains("-c:v copy", args);
        Assert.Contains("-c:a copy", args);
        Assert.Contains("-movflags +faststart", args);
        Assert.DoesNotContain("-c:a aac", args);
        Assert.DoesNotContain("-b:a", args);
        Assert.EndsWith("\"C:\\out.mp4\"", args);
    }

    [Fact]
    public void ForRemuxOpus_CopiesAudioStreamWithoutReencode()
    {
        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus");

        Assert.StartsWith("-y", args);
        Assert.Contains("-i \"C:\\in.webm\"", args);
        Assert.Contains("-vn", args);
        Assert.Contains("-c:a copy", args);
        Assert.DoesNotContain("libopus", args);
        Assert.EndsWith("\"C:\\out.opus\"", args);
    }
}
