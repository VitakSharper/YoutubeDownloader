namespace YoutubeDownloader.Core.Services;

public interface IMediaConverter
{
    Task ConvertToMp3Async(string ffmpegPath, string input, string output, int bitrateKbps,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4Async(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
}
