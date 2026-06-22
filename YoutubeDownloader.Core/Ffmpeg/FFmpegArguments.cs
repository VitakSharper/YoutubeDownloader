namespace YoutubeDownloader.Core.Ffmpeg;

public static class FFmpegArguments
{
    public static string ForMp3(string input, string output, int bitrateKbps)
        => $"-y -i \"{input}\" -vn -c:a libmp3lame -b:a {bitrateKbps}k \"{output}\"";

    public static string ForMuxMp4(string videoInput, string audioInput, string output)
        => $"-y -i \"{videoInput}\" -i \"{audioInput}\" -c:v copy -c:a aac -movflags +faststart \"{output}\"";
}
