namespace YoutubeDownloader.Core.Ffmpeg;

public static class FFmpegArguments
{
    public static string ForMp3Cbr(string input, string output, int bitrateKbps)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{output}\"";

    public static string ForMp3Vbr(string input, string output, int vbrQuality)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -q:a {vbrQuality} \"{output}\"";

    public static string ForMuxMp4(string videoInput, string audioInput, string output)
        => $"-y -i \"{videoInput}\" -i \"{audioInput}\" -c:v copy -c:a aac -b:a 192k -movflags +faststart \"{output}\"";

    public static string ForMuxMp4CopyAudio(string videoInput, string audioInput, string output)
        => $"-y -i \"{videoInput}\" -i \"{audioInput}\" -c:v copy -c:a copy -movflags +faststart \"{output}\"";

    public static string ForRemuxOpus(string input, string output)
        => $"-y -i \"{input}\" -vn -c:a copy \"{output}\"";
}
