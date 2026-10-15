using YoutubeDownloader.Core.Ffmpeg;
using YoutubeDownloader.Core.Models;

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

    private static readonly MediaTags SampleTags =
        new("Song Title", "Some Channel", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", 2009);

    [Fact]
    public void ForMp3Vbr_WithTagsAndCover_MapsCoverWritesTagsAndOmitsVn()
    {
        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0, SampleTags, @"C:\cover.webp");

        // Exactly the argument list proven on FFmpeg 6.1 in the plan 015 spike.
        string[] expected =
        [
            "-y", "-i", @"C:\in.webm", "-i", @"C:\cover.webp", "-map", "0:a", "-map", "1:v",
            "-c:a", "libmp3lame", "-q:a", "0",
            "-c:v", "mjpeg", "-disposition:v:0", "attached_pic",
            "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)",
            "-id3v2_version", "3",
            "-metadata", "title=Song Title", "-metadata", "artist=Some Channel",
            "-metadata", "comment=https://www.youtube.com/watch?v=dQw4w9WgXcQ", "-metadata", "date=2009",
            @"C:\out.mp3"
        ];
        Assert.Equal(expected, args);
        Assert.DoesNotContain("-vn", args);
        Assert.Equal(@"C:\out.mp3", args[^1]);
    }

    [Fact]
    public void ForMp3Cbr_WithTagsNoCover_KeepsVnAndWritesTags()
    {
        var args = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192, SampleTags);

        Assert.Contains("-vn", args);
        Assert.DoesNotContain("-map", args);
        Assert.DoesNotContain("mjpeg", args);
        Assert.Single(args, a => a == "-i");
        AssertPair(args, "-b:a", "192k");
        AssertPair(args, "-id3v2_version", "3");
        AssertPair(args, "-metadata", "title=Song Title");
        AssertPair(args, "-metadata", "artist=Some Channel");
        AssertPair(args, "-metadata", "comment=https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        AssertPair(args, "-metadata", "date=2009");
        Assert.Equal(@"C:\out.mp3", args[^1]);
    }

    [Fact]
    public void ForMp3Builders_WithoutTags_AreUnchanged()
    {
        var cbr = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192, tags: null, coverPath: null);
        var vbr = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 2, tags: null, coverPath: null);

        string[] expectedCbr = ["-y", "-i", @"C:\in.webm", "-vn", "-c:a", "libmp3lame", "-b:a", "192k", @"C:\out.mp3"];
        string[] expectedVbr = ["-y", "-i", @"C:\in.webm", "-vn", "-c:a", "libmp3lame", "-q:a", "2", @"C:\out.mp3"];
        Assert.Equal(expectedCbr, cbr);
        Assert.Equal(expectedVbr, vbr);
        Assert.DoesNotContain("-metadata", cbr);
        Assert.DoesNotContain("-id3v2_version", cbr);
        Assert.DoesNotContain("-metadata", vbr);
        Assert.DoesNotContain("-id3v2_version", vbr);
        Assert.Equal(@"C:\out.mp3", cbr[^1]);
        Assert.Equal(@"C:\out.mp3", vbr[^1]);
    }

    [Fact]
    public void ForRemuxOpus_WithTags_WritesTagPairsAndKeepsCopy()
    {
        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus", SampleTags);

        Assert.Contains("-vn", args);
        AssertPair(args, "-c:a", "copy");
        AssertPair(args, "-metadata", "title=Song Title");
        AssertPair(args, "-metadata", "artist=Some Channel");
        AssertPair(args, "-metadata", "comment=https://www.youtube.com/watch?v=dQw4w9WgXcQ");
        AssertPair(args, "-metadata", "date=2009");
        Assert.DoesNotContain("-id3v2_version", args); // ID3 is MP3-only
        Assert.Equal(@"C:\out.opus", args[^1]);
    }

    [Fact]
    public void Tags_WithQuotesAmpersandAndUnicode_AreSingleVerbatimTokens()
    {
        var tags = new MediaTags("He said \"hi\" & ünïcode", "Chännel \"X\"", "https://youtu.be/abc", null);

        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0, tags);

        Assert.Contains("title=He said \"hi\" & ünïcode", args);
        Assert.Contains("artist=Chännel \"X\"", args);
        Assert.Equal(@"C:\out.mp3", args[^1]);
    }

    [Fact]
    public void Tags_SkipEmptyArtistAndNullYear()
    {
        var tags = new MediaTags("Song Title", " ", "https://youtu.be/abc", null);

        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus", tags);

        AssertPair(args, "-metadata", "title=Song Title");
        AssertPair(args, "-metadata", "comment=https://youtu.be/abc");
        Assert.DoesNotContain(args, a => a.StartsWith("artist=", StringComparison.Ordinal));
        Assert.DoesNotContain(args, a => a.StartsWith("date=", StringComparison.Ordinal));
        Assert.Equal(2, args.Count(a => a == "-metadata"));
        Assert.Equal(@"C:\out.opus", args[^1]);
    }

    [Fact]
    public void ForMp3Vbr_WithAlbumAndTrack_WritesAlbumAndTrackOfTotal()
    {
        var tags = SampleTags with { Album = "'80s Sing-Alongs", Track = 3, TrackCount = 86 };

        var args = FFmpegArguments.ForMp3Vbr(@"C:\in.webm", @"C:\out.mp3", 0, tags, @"C:\cover.webp");

        // The album pairs come after date, so untagged files keep the exact 015 argument list.
        string[] tail =
        [
            "-metadata", "date=2009",
            "-metadata", "album='80s Sing-Alongs",
            "-metadata", "track=3/86",
            @"C:\out.mp3"
        ];
        Assert.Equal(tail, args.Skip(args.Count - tail.Length));
        Assert.Single(args, a => a == "-id3v2_version");
        AssertPair(args, "-id3v2_version", "3");
    }

    [Fact]
    public void ForRemuxOpus_WithAlbumAndTrack_WritesThem()
    {
        var tags = SampleTags with { Album = "Pharmacology", Track = 12, TrackCount = 50 };

        var args = FFmpegArguments.ForRemuxOpus(@"C:\in.webm", @"C:\out.opus", tags);

        AssertPair(args, "-c:a", "copy");
        AssertPair(args, "-metadata", "date=2009");
        AssertPair(args, "-metadata", "album=Pharmacology");
        AssertPair(args, "-metadata", "track=12/50");
        Assert.Equal(6, args.Count(a => a == "-metadata"));
        Assert.DoesNotContain("-id3v2_version", args); // ID3 is MP3-only
        Assert.Equal(@"C:\out.opus", args[^1]);
    }

    [Fact]
    public void Tags_TrackWithoutTotal_WritesThePlainNumber()
    {
        var tags = SampleTags with { Track = 7 };

        var args = FFmpegArguments.ForMp3Cbr(@"C:\in.webm", @"C:\out.mp3", 192, tags);

        AssertPair(args, "-metadata", "track=7");
        Assert.DoesNotContain(args, a => a.StartsWith("album=", StringComparison.Ordinal));
        Assert.Equal(5, args.Count(a => a == "-metadata"));
        Assert.Equal(@"C:\out.mp3", args[^1]);
    }

    private static void AssertPair(IReadOnlyList<string> args, string flag, string value)
    {
        var found = Enumerable.Range(0, args.Count - 1).Any(i => args[i] == flag && args[i + 1] == value);
        Assert.True(found, $"Expected '{flag} {value}' in: {string.Join(" | ", args)}");
    }
}
