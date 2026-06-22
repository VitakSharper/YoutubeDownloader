namespace YoutubeDownloader.Core.Services;

public sealed class FFmpegLocator : IFFmpegLocator
{
    private readonly IFFmpegBinaryDownloader _downloader;
    private readonly string _installDir;

    public FFmpegLocator(IFFmpegBinaryDownloader downloader, string installDir)
    {
        _downloader = downloader;
        _installDir = installDir;
    }

    public async Task<string> EnsureAsync(IProgress<double>? progress, CancellationToken ct)
    {
        var ffmpegPath = Path.Combine(_installDir, "ffmpeg.exe");
        if (File.Exists(ffmpegPath))
        {
            progress?.Report(1.0);
            return ffmpegPath;
        }

        Directory.CreateDirectory(_installDir);
        await _downloader.DownloadAsync(_installDir, progress, ct);

        if (!File.Exists(ffmpegPath))
            throw new FileNotFoundException("FFmpeg download completed but ffmpeg.exe was not found.", ffmpegPath);

        return ffmpegPath;
    }
}
