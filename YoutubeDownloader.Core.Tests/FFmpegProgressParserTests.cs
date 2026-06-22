using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegProgressParserTests
{
    [Fact]
    public void TryParseTimeSeconds_ParsesHmsMillis()
    {
        var ok = FFmpegProgressParser.TryParseTimeSeconds(
            "frame= 120 fps=30 q=28.0 size=2048kB time=00:01:05.50 bitrate=...", out var s);

        Assert.True(ok);
        Assert.Equal(65.5, s, precision: 2);
    }

    [Theory]
    [InlineData("ffmpeg version 6.0 Copyright (c)")]
    [InlineData("")]
    [InlineData("time=N/A")]
    public void TryParseTimeSeconds_NoTime_ReturnsFalse(string line)
    {
        Assert.False(FFmpegProgressParser.TryParseTimeSeconds(line, out _));
    }
}
