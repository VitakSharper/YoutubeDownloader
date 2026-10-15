using System.Diagnostics;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Services;

/// <summary>
/// One video → one file: stream(s) to temp files, FFmpeg into the ".part" sibling of the target, then commit.
/// No ConfigureAwait(false): progress must keep flowing on the caller's context (the UI thread).
/// </summary>
public sealed class DownloadPipeline : IDownloadPipeline
{
    /// <summary>Share of the 0..1 pipeline range used by the stream download(s); FFmpeg gets the rest.</summary>
    internal const double DownloadShare = 2.0 / 3.0;

    internal const string NoOpusStreamMessage = "This video has no Opus audio stream to save.";

    private readonly IYouTubeService _youtube;
    private readonly IMediaConverter _converter;
    private readonly ITempFileService _temp;
    private readonly IOutputFileService _output;
    private readonly IThumbnailFetcher _thumbnails;

    public DownloadPipeline(IYouTubeService youtube, IMediaConverter converter, ITempFileService temp,
        IOutputFileService output, IThumbnailFetcher thumbnails)
    {
        _youtube = youtube;
        _converter = converter;
        _temp = temp;
        _output = output;
        _thumbnails = thumbnails;
    }

    public async Task<DownloadOutcome> RunAsync(DownloadRequest request, IProgress<PipelineProgress> progress, CancellationToken ct)
    {
        if (request.Format is OutputFormat.Opus && request.Info.BestOpusAudio is null)
            return new DownloadOutcome.Failed(NoOpusStreamMessage);

        var stages = new StageOrderedProgress(progress);
        var part = PartFilePath.For(request.TargetPath);
        var committed = false;
        var tempFiles = new List<string>();
        try
        {
            var formatLabel = request.Format switch
            {
                OutputFormat.Mp3 mp3 => await SaveMp3Async(request, mp3.Quality, part, tempFiles, stages, ct),
                OutputFormat.Opus => await SaveOpusAsync(request, part, tempFiles, stages, ct),
                OutputFormat.Mp4 mp4 => await SaveMp4Async(request, mp4.Video, part, tempFiles, stages, ct),
                _ => throw new UnreachableException()
            };
            _output.Commit(part, request.TargetPath);
            committed = true;
            return new DownloadOutcome.Saved(request.TargetPath, formatLabel);
        }
        catch (OperationCanceledException)
        {
            return new DownloadOutcome.Cancelled();
        }
        catch (Exception ex)
        {
            return new DownloadOutcome.Failed(ex.Message, ex);
        }
        finally
        {
            if (!committed)
                _output.Discard(part);
            DeleteTempFiles(tempFiles);
        }
    }

    private async Task<string> SaveMp3Async(DownloadRequest request, Mp3Quality quality, string part,
        List<string> tempFiles, IProgress<PipelineProgress> stages, CancellationToken ct)
    {
        var info = request.Info;
        stages.Report(new PipelineProgress(PipelineStage.DownloadingAudio, 0));
        var tempAudio = _temp.NewTempFile(info.BestAudio.Container);
        tempFiles.Add(tempAudio);
        await _youtube.DownloadStreamAsync(info.BestAudio.Source, tempAudio,
            DownloadProgress(stages, PipelineStage.DownloadingAudio), ct);

        var cover = await _thumbnails.TryDownloadAsync(info.CoverArtUrl ?? info.ThumbnailUrl, ct);
        if (cover is not null)
            tempFiles.Add(cover);

        stages.Report(new PipelineProgress(PipelineStage.Converting, DownloadShare));
        var convertProgress = FfmpegProgress(stages, PipelineStage.Converting);
        try
        {
            await _converter.ConvertToMp3Async(request.FfmpegPath, tempAudio, part, quality, request.Tags, cover,
                info.Duration, convertProgress, ct);
        }
        catch (Exception ex) when (cover is not null && ex is not OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Cover art is best-effort: an image FFmpeg can't use must not cost the download.
            await _converter.ConvertToMp3Async(request.FfmpegPath, tempAudio, part, quality, request.Tags, null,
                info.Duration, convertProgress, ct);
        }
        return $"MP3 · {quality.HistoryLabel}";
    }

    private async Task<string> SaveOpusAsync(DownloadRequest request, string part,
        List<string> tempFiles, IProgress<PipelineProgress> stages, CancellationToken ct)
    {
        var info = request.Info;
        stages.Report(new PipelineProgress(PipelineStage.DownloadingAudio, 0));
        var tempAudio = _temp.NewTempFile(info.BestOpusAudio!.Container);
        tempFiles.Add(tempAudio);
        await _youtube.DownloadStreamAsync(info.BestOpusAudio.Source, tempAudio,
            DownloadProgress(stages, PipelineStage.DownloadingAudio), ct);

        stages.Report(new PipelineProgress(PipelineStage.SavingOpus, DownloadShare));
        await _converter.RemuxToOpusAsync(request.FfmpegPath, tempAudio, part, request.Tags,
            info.Duration, FfmpegProgress(stages, PipelineStage.SavingOpus), ct);
        return $"Opus · {info.BestOpusAudio.BitrateKbps} kbps";
    }

    private async Task<string> SaveMp4Async(DownloadRequest request, VideoStreamOption video, string part,
        List<string> tempFiles, IProgress<PipelineProgress> stages, CancellationToken ct)
    {
        var info = request.Info;
        stages.Report(new PipelineProgress(PipelineStage.DownloadingVideo, 0));
        var audio = info.BestMp4Audio ?? info.BestAudio;
        var tempVideo = _temp.NewTempFile(video.Container);
        tempFiles.Add(tempVideo);
        var tempAudio = _temp.NewTempFile(audio.Container);
        tempFiles.Add(tempAudio);

        // Video and audio are independent streams: fetch both at once.
        // Progress window 0–DownloadShare is shared, weighted 80% video / 20% audio.
        var streams = new CombinedProgress(firstWeight: 0.8);
        using (var streamsCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            var videoTask = DownloadCancellingSiblingAsync(video.Source, tempVideo,
                new Progress<double>(p => stages.Report(
                    new PipelineProgress(PipelineStage.DownloadingVideo, streams.ReportFirst(p) * DownloadShare))),
                streamsCts);
            var audioTask = DownloadCancellingSiblingAsync(audio.Source, tempAudio,
                new Progress<double>(p => stages.Report(
                    new PipelineProgress(PipelineStage.DownloadingVideo, streams.ReportSecond(p) * DownloadShare))),
                streamsCts);
            await Task.WhenAll(videoTask, audioTask);
        }

        stages.Report(new PipelineProgress(PipelineStage.Merging, DownloadShare));
        if (info.BestMp4Audio is not null)
            await _converter.MuxToMp4CopyAudioAsync(request.FfmpegPath, tempVideo, tempAudio, part,
                info.Duration, FfmpegProgress(stages, PipelineStage.Merging), ct);
        else
            await _converter.MuxToMp4Async(request.FfmpegPath, tempVideo, tempAudio, part,
                info.Duration, FfmpegProgress(stages, PipelineStage.Merging), ct);
        return $"MP4 · {video.QualityLabel}";
    }

    /// <summary>
    /// Downloads one stream of a parallel pair. If it fails (or is cancelled), it cancels the
    /// sibling download too, so no download keeps writing a temp file that cleanup is deleting.
    /// </summary>
    private async Task DownloadCancellingSiblingAsync(IStreamInfo source, string filePath,
        IProgress<double> progress, CancellationTokenSource siblings)
    {
        try
        {
            await _youtube.DownloadStreamAsync(source, filePath, progress, siblings.Token);
        }
        catch
        {
            siblings.Cancel();
            throw;
        }
    }

    private static IProgress<double> DownloadProgress(IProgress<PipelineProgress> stages, PipelineStage stage)
        => new Progress<double>(p => stages.Report(new PipelineProgress(stage, p * DownloadShare)));

    private static IProgress<double> FfmpegProgress(IProgress<PipelineProgress> stages, PipelineStage stage)
        => new Progress<double>(p => stages.Report(new PipelineProgress(stage, DownloadShare + p * (1 - DownloadShare))));

    private static void DeleteTempFiles(IEnumerable<string> tempFiles)
    {
        foreach (var f in tempFiles)
        {
            try { if (File.Exists(f)) File.Delete(f); }
            catch { /* best-effort temp cleanup */ }
        }
    }
}
