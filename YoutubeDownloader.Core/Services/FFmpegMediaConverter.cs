using System.Diagnostics;
using YoutubeDownloader.Core.Ffmpeg;

namespace YoutubeDownloader.Core.Services;

public sealed class FFmpegMediaConverter : IMediaConverter
{
    public Task ConvertToMp3Async(string ffmpegPath, string input, string output, int bitrateKbps,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
        => RunAsync(ffmpegPath, FFmpegArguments.ForMp3(input, output, bitrateKbps), duration, progress, ct);

    public Task MuxToMp4Async(string ffmpegPath, string videoInput, string audioInput, string output,
        TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
        => RunAsync(ffmpegPath, FFmpegArguments.ForMuxMp4(videoInput, audioInput, output), duration, progress, ct);

    private static async Task RunAsync(string ffmpegPath, string arguments, TimeSpan duration,
        IProgress<double>? progress, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = arguments,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var totalSeconds = duration.TotalSeconds;

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null || totalSeconds <= 0 || progress is null)
                return;
            if (FFmpegProgressParser.TryParseTimeSeconds(e.Data, out var seconds))
                progress.Report(Math.Clamp(seconds / totalSeconds, 0.0, 1.0));
        };

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start FFmpeg at '{ffmpegPath}'.");

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"FFmpeg exited with code {process.ExitCode}.");

        progress?.Report(1.0);
    }
}
