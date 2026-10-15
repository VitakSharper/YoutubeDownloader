using System.Diagnostics;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Helpers;

public static class PipelineStatusText
{
    /// <summary>Status-row text for a pipeline stage, identical to the single download's texts before plan 021.</summary>
    public static string For(PipelineStage stage, OutputFormat format) => stage switch
    {
        PipelineStage.DownloadingAudio => "Downloading audio…",
        PipelineStage.DownloadingVideo => format is OutputFormat.Mp4 mp4
            ? $"Downloading video ({mp4.Video.QualityLabel})…"
            : "Downloading video…",
        PipelineStage.Converting => format is OutputFormat.Mp3 mp3
            ? $"Converting to MP3 ({mp3.Quality.Label})…"
            : "Converting…",
        PipelineStage.SavingOpus => "Saving Opus audio…",
        PipelineStage.Merging => "Merging video + audio…",
        _ => throw new UnreachableException()
    };
}
