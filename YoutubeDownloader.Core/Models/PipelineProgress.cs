namespace YoutubeDownloader.Core.Models;

/// <summary>
/// Pipeline steps. The declaration order is the order the steps run in for every format
/// (MP3: DownloadingAudio → Converting; Opus: DownloadingAudio → SavingOpus; MP4: DownloadingVideo → Merging).
/// StageOrderedProgress relies on it: do not reorder.
/// </summary>
public enum PipelineStage { DownloadingAudio, DownloadingVideo, Converting, SavingOpus, Merging }

/// <param name="Fraction">0..1 over the whole pipeline: the stream download(s) use 0..2/3, FFmpeg 2/3..1.</param>
public readonly record struct PipelineProgress(PipelineStage Stage, double Fraction);
