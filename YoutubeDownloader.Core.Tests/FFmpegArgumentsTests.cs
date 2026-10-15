using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegArgumentsTests
{
    [Fact]
    public void ForMp3Cbr_BuildsLameCbrArgsWithBitrateAndQuotedPaths()
    {
        var args = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192);

        AssertPair(args, "-i", @"C:\in.webm");
        Assert.Contains("-vn", args);
        AssertPair(args, "-c:a", "libmp3lame");
        AssertPair(args, "-b:a", "192k");
        Assert.Equal(@"C:\out.mp3", args[^1]);
        Assert.Equal("-y", args[0]);
    }

    [Fact]
    public void ForMp3Vbr_BuildsLameVbrArgsWithQualityLevel()
    {
        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0);

        AssertPair(args, "-i", @"C:\in.webm");
        Assert.Contains("-vn", args);
        AssertPair(args, "-c:a", "libmp3lame");
        AssertPair(args, "-q:a", "0");
        Assert.DoesNotContain("-b:a", args);
        Assert.Equal(@"C:\out.mp3", args[^1]);
        Assert.Equal("-y", args[0]);
    }

    [Fact]
    public void ForMuxMp4_CopiesVideoAndEncodesAacWithFaststart()
    {
        var args = FFmpegArguments.ForMuxMp4(@"C:\v.mp4", @"C:\a.m4a", @"C:\out.mp4");

        AssertPair(args, "-i", @"C:\v.mp4");
        AssertPair(args, "-i", @"C:\a.m4a");
        AssertPair(args, "-c:v", "copy");
        AssertPair(args, "-c:a", "aac");
        AssertPair(args, "-b:a", "192k");
        AssertPair(args, "-movflags", "+faststart");
        Assert.Equal(@"C:\out.mp4", args[^1]);
    }

    [Fact]
    public void ForMuxMp4CopyAudio_CopiesBothStreamsWithFaststart()
    {
        var args = FFmpegArguments.ForMuxMp4CopyAudio(@"C:\v.mp4", @"C:\a.mp4", @"C:\out.mp4");

        Assert.Equal("-y", args[0]);
        AssertPair(args, "-i", @"C:\v.mp4");
        AssertPair(args, "-i", @"C:\a.mp4");
        AssertPair(args, "-c:v", "copy");
        AssertPair(args, "-c:a", "copy");
        AssertPair(args, "-movflags", "+faststart");
        Assert.DoesNotContain("aac", args);
        Assert.DoesNotContain("-b:a", args);
        Assert.Equal(@"C:\out.mp4", args[^1]);
    }

    [Fact]
    public void ForRemuxOpus_CopiesAudioStreamWithoutReencode()
    {
        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus");

        Assert.Equal("-y", args[0]);
        AssertPair(args, "-i", @"C:\in.webm");
        Assert.Contains("-vn", args);
        AssertPair(args, "-c:a", "copy");
        Assert.DoesNotContain("libopus", args);
        Assert.Equal(@"C:\out.opus", args[^1]);
    }

    private static void AssertPair(IReadOnlyList<string> args, string flag, string value)
    {
        var found = Enumerable.Range(0, args.Count - 1).Any(i => args[i] == flag && args[i + 1] == value);
        Assert.True(found, $"Expected '{flag} {value}' in: {string.Join(" | ", args)}");
    }
}
