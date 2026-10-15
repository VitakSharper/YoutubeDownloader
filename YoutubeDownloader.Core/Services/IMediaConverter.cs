using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IMediaConverter
{
    Task ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4Async(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4CopyAudioAsync(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task RemuxToOpusAsync(string ffmpegPath, string input, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
}
