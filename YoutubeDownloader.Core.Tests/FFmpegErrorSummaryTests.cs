using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Tests;

public class FFmpegErrorSummaryTests
{
    [Fact]
    public void From_RealFailureTail_SkipsGenericTrailerAndKeepsCause()
    {
        var lines = new[]
        {
            "size=     256kB time=00:00:10.00 bitrate= 209.7kbits/s speed=20x",
            "[mp3 @ 000001] Invalid audio stream. Exactly one MP3 audio stream is required.",
            "Could not write header for output file #0 (incorrect codec parameters ?): Invalid argument",
            "Error initializing output stream 0:0 --",
            "Conversion failed!"
        };

        var summary = FFmpegErrorSummary.From(lines);

        Assert.Equal("Could not write header for output file #0 (incorrect codec parameters ?): Invalid argument | Error initializing output stream 0:0 --", summary);
    }

    [Fact]
    public void From_OnlyProgressAndBlankLines_ReturnsEmpty()
    {
        Assert.Equal("", FFmpegErrorSummary.From(new[] { "", "  ", "frame=  120 fps= 30 q=-1.0 size=1024kB time=00:00:04.00" }));
    }

    [Fact]
    public void From_LongLine_TruncatesWithEllipsis()
    {
        var summary = FFmpegErrorSummary.From(new[] { new string('x', 500) }, maxLength: 200);

        Assert.Equal(200, summary.Length);
        Assert.EndsWith("…", summary);
    }

    [Fact]
    public void From_SingleMeaningfulLine_ReturnedAsIs()
    {
        Assert.Equal("C:\\in.webm: Invalid data found when processing input",
            FFmpegErrorSummary.From(new[] { "C:\\in.webm: Invalid data found when processing input" }));
    }
}
