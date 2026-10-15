using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Tests;

public class PipelineHelpersTests
{
    [Fact]
    public void StageOrderedProgress_DropsReportFromEarlierStage()
    {
        var seen = new List<PipelineProgress>();
        var sut = new StageOrderedProgress(new InlineProgress<PipelineProgress>(seen.Add));

        sut.Report(new PipelineProgress(PipelineStage.Converting, 0.8));
        sut.Report(new PipelineProgress(PipelineStage.DownloadingAudio, 0.5));

        Assert.Equal(new[] { new PipelineProgress(PipelineStage.Converting, 0.8) }, seen);
    }

    [Fact]
    public void StageOrderedProgress_ForwardsSameAndLaterStages()
    {
        var seen = new List<PipelineProgress>();
        var sut = new StageOrderedProgress(new InlineProgress<PipelineProgress>(seen.Add));
        var reports = new[]
        {
            new PipelineProgress(PipelineStage.DownloadingAudio, 0.1),
            new PipelineProgress(PipelineStage.DownloadingAudio, 0.3),
            new PipelineProgress(PipelineStage.Converting, 0.7)
        };

        foreach (var report in reports)
            sut.Report(report);

        Assert.Equal(reports, seen);
    }

    [Theory]
    [InlineData(PipelineStage.DownloadingAudio, "Downloading audio…")]
    [InlineData(PipelineStage.SavingOpus, "Saving Opus audio…")]
    [InlineData(PipelineStage.Merging, "Merging video + audio…")]
    public void PipelineStatusText_FormatIndependentStages(PipelineStage stage, string expected)
    {
        Assert.Equal(expected, PipelineStatusText.For(stage, new OutputFormat.Opus()));
    }

    [Fact]
    public void PipelineStatusText_DownloadingVideo_NamesTheQuality()
    {
        var format = new OutputFormat.Mp4(new VideoStreamOption("720p", 720, "mp4", Source: null!));

        Assert.Equal("Downloading video (720p)…", PipelineStatusText.For(PipelineStage.DownloadingVideo, format));
    }

    [Fact]
    public void PipelineStatusText_Converting_NamesTheMp3Preset()
    {
        var format = new OutputFormat.Mp3(Mp3Quality.BestVbr);

        Assert.Equal("Converting to MP3 (Best (VBR V0))…", PipelineStatusText.For(PipelineStage.Converting, format));
    }

    [Fact]
    public void InlineProgress_RunsHandlerSynchronously()
    {
        int? captured = null;
        int? handlerThreadId = null;
        var sut = new InlineProgress<int>(value =>
        {
            captured = value;
            handlerThreadId = Environment.CurrentManagedThreadId;
        });

        sut.Report(5);

        Assert.Equal(5, captured);
        Assert.Equal(Environment.CurrentManagedThreadId, handlerThreadId);
    }
}
