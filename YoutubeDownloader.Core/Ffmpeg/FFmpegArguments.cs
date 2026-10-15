using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Ffmpeg;

/// <summary>
/// FFmpeg command lines as argv tokens (passed through <c>ProcessStartInfo.ArgumentList</c>),
/// so values that come from YouTube never need hand-quoting.
/// </summary>
public static class FFmpegArguments
{
    /// <summary>
    /// Re-encodes the second input (jpg/webp/…) to a JPEG front cover (APIC frame).
    /// Proven on FFmpeg 6.1: <c>-pix_fmt</c> is not needed; the disposition is kept as in the ffmpeg docs.
    /// </summary>
    private static readonly string[] CoverArguments =
    [
        "-c:v", "mjpeg", "-disposition:v:0", "attached_pic",
        "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)"
    ];

    /// <summary>ID3v2.3 rather than FFmpeg's default 2.4, for Windows Explorer / older players.</summary>
    private static readonly string[] Id3v23Arguments = ["-id3v2_version", "3"];

    public static IReadOnlyList<string> ForMp3Cbr(string input, string output, int bitrateKbps,
        MediaTags? tags = null, string? coverPath = null)
        => ForMp3(input, output, ["-c:a", "libmp3lame", "-b:a", $"{bitrateKbps}k"], tags, coverPath);

    public static IReadOnlyList<string> ForMp3Vbr(string input, string output, int vbrQuality,
        MediaTags? tags = null, string? coverPath = null)
        => ForMp3(input, output, ["-c:a", "libmp3lame", "-q:a", $"{vbrQuality}"], tags, coverPath);

    public static IReadOnlyList<string> ForMuxMp4(string videoInput, string audioInput, string output)
        => ["-y", "-i", videoInput, "-i", audioInput, "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output];

    public static IReadOnlyList<string> ForMuxMp4CopyAudio(string videoInput, string audioInput, string output)
        => ["-y", "-i", videoInput, "-i", audioInput, "-c:v", "copy", "-c:a", "copy", "-movflags", "+faststart", output];

    public static IReadOnlyList<string> ForRemuxOpus(string input, string output, MediaTags? tags = null)
        => ["-y", "-i", input, "-vn", "-c:a", "copy", .. TagArguments(tags), output];

    /// <summary>
    /// With a cover the picture is mapped as a second stream, so there is no <c>-vn</c> (it would drop the cover).
    /// Without tags and cover the command line is the plain conversion.
    /// </summary>
    private static IReadOnlyList<string> ForMp3(string input, string output, string[] audioCodec,
        MediaTags? tags, string? coverPath)
    {
        var hasCover = !string.IsNullOrWhiteSpace(coverPath);
        string[] inputs = hasCover
            ? ["-y", "-i", input, "-i", coverPath!, "-map", "0:a", "-map", "1:v"]
            : ["-y", "-i", input, "-vn"];

        return
        [
            .. inputs,
            .. audioCodec,
            .. (hasCover ? CoverArguments : []),
            .. (hasCover || tags is not null ? Id3v23Arguments : []),
            .. TagArguments(tags),
            output
        ];
    }

    /// <summary>One <c>-metadata key=value</c> pair per known, non-blank value; each value is a single token.</summary>
    private static IEnumerable<string> TagArguments(MediaTags? tags)
    {
        if (tags is null)
            yield break;

        (string Key, string? Value)[] pairs =
        [
            ("title", tags.Title),
            ("artist", tags.Artist),
            ("comment", tags.SourceUrl),
            ("date", tags.Year?.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            // After date, so files without album tags keep byte-identical arguments (plan 027a).
            ("album", tags.Album),
            ("track", TrackText(tags))
        ];

        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;
            yield return "-metadata";
            yield return $"{key}={value}";
        }
    }

    /// <summary>"3/12" with the album's track count (the ID3 TRCK convention), "3" without it, null without a track.</summary>
    private static string? TrackText(MediaTags tags) =>
        tags.Track is not { } track ? null
        : tags.TrackCount is { } count ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{track}/{count}")
        : track.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
