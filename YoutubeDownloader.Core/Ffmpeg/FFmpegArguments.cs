namespace YoutubeDownloader.Core.Ffmpeg;

/// <summary>
/// FFmpeg command lines as argv tokens (passed through <c>ProcessStartInfo.ArgumentList</c>),
/// so values that come from YouTube never need hand-quoting.
/// </summary>
public static class FFmpegArguments
{
    public static IReadOnlyList<string> ForMp3Cbr(string input, string output, int bitrateKbps)
        => ["-y", "-i", input, "-vn", "-c:a", "libmp3lame", "-b:a", $"{bitrateKbps}k", output];

    public static IReadOnlyList<string> ForMp3Vbr(string input, string output, int vbrQuality)
        => ["-y", "-i", input, "-vn", "-c:a", "libmp3lame", "-q:a", $"{vbrQuality}", output];

    public static IReadOnlyList<string> ForMuxMp4(string videoInput, string audioInput, string output)
        => ["-y", "-i", videoInput, "-i", audioInput, "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart", output];

    public static IReadOnlyList<string> ForMuxMp4CopyAudio(string videoInput, string audioInput, string output)
        => ["-y", "-i", videoInput, "-i", audioInput, "-c:v", "copy", "-c:a", "copy", "-movflags", "+faststart", output];

    public static IReadOnlyList<string> ForRemuxOpus(string input, string output)
        => ["-y", "-i", input, "-vn", "-c:a", "copy", output];
}
