using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public interface IMediaConverter
{
    /// <param name="tags">Text tags to write, or null for none.</param>
    /// <param name="coverPath">Image to embed as the front cover, or null for none.</param>
    Task ConvertToMp3Async(string ffmpegPath, string input, string output, Mp3Quality quality,
        MediaTags? tags, string? coverPath,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4Async(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    Task MuxToMp4CopyAudioAsync(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);

    /// <param name="tags">Text tags to write, or null for none.</param>
    Task RemuxToOpusAsync(string ffmpegPath, string input, string output, MediaTags? tags,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct);
}
