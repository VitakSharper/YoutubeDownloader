using System.Diagnostics;
using Moq;
using YoutubeExplode.Videos.Streams;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class DownloadPipelineTests
{
    private const string FfmpegPath = @"C:\ffmpeg\ffmpeg.exe";

    private readonly Mock<IYouTubeService> _youtube = new();
    private readonly Mock<IMediaConverter> _converter = new();
    private readonly Mock<ITempFileService> _temp = new();
    private readonly Mock<IOutputFileService> _output = new();
    private readonly Mock<IThumbnailFetcher> _thumbnails = new();
    private readonly List<PipelineProgress> _reports = new();
    private readonly InlineProgress<PipelineProgress> _progress;

    public DownloadPipelineTests()
    {
        _progress = new InlineProgress<PipelineProgress>(p => { lock (_reports) _reports.Add(p); });
    }

    private DownloadPipeline CreateSut() =>
        new(_youtube.Object, _converter.Object, _temp.Object, _output.Object, _thumbnails.Object);

    private static readonly IStreamInfo AacSource = Mock.Of<IStreamInfo>();

    private static VideoInfo SampleInfo(bool withOpus = true, bool withAac = false) => new(
        SourceUrl: "https://youtu.be/dQw4w9WgXcQ",
        Title: "Test Video",
        Author: "Tester",
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions: new[] { new VideoStreamOption("720p", 720, "mp4", Source: null!) },
        BestOpusAudio: withOpus ? new AudioStreamOption("opus", 160, "webm", Source: null!) : null,
        BestMp4Audio: withAac ? new AudioStreamOption("mp4a.40.2", 128, "mp4", Source: AacSource) : null);

    private static DownloadRequest Request(OutputFormat format, VideoInfo? info = null)
    {
        var ext = format switch
        {
            OutputFormat.Mp3 => "mp3",
            OutputFormat.Opus => "opus",
            OutputFormat.Mp4 => "mp4",
            _ => throw new UnreachableException()
        };
        var video = info ?? SampleInfo();
        var tags = new MediaTags(video.Title, video.Author, video.SourceUrl, null);
        return new DownloadRequest(video, format, $@"C:\out\song.{ext}", tags, FfmpegPath);
    }

    private static OutputFormat.Mp4 Mp4Of(VideoInfo info) => new(info.VideoOptions[0]);

    [Fact]
    public async Task Mp3_Success_ConvertsToPartCommitsAndReturnsSaved()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        var request = Request(new OutputFormat.Mp3(Mp3Quality.Cbr256));

        var outcome = await CreateSut().RunAsync(request, _progress, CancellationToken.None);

        _converter.Verify(c => c.ConvertToMp3Async(FfmpegPath, @"C:\temp\a.webm", @"C:\out\song.part.mp3", Mp3Quality.Cbr256,
            request.Tags, It.IsAny<string?>(), request.Info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _output.Verify(o => o.Commit(@"C:\out\song.part.mp3", @"C:\out\song.mp3"), Times.Once);
        Assert.Equal(new DownloadOutcome.Saved(@"C:\out\song.mp3", "MP3 · 256 kbps"), outcome);
    }

    [Fact]
    public async Task Mp3_ConversionWithCoverFails_RetriesWithoutCover()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _thumbnails.Setup(t => t.TryDownloadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(@"C:\temp\cover.jpg");
        _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
                It.IsAny<MediaTags?>(), @"C:\temp\cover.jpg",
                It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("FFmpeg exited with code 1: bad cover"));

        var outcome = await CreateSut().RunAsync(Request(new OutputFormat.Mp3(Mp3Quality.BestVbr)), _progress, CancellationToken.None);

        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.IsAny<MediaTags?>(), @"C:\temp\cover.jpg",
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Mp3Quality>(),
            It.Is<MediaTags?>(t => t != null), null,
            It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsType<DownloadOutcome.Saved>(outcome);
    }

    [Fact]
    public async Task Mp3_ReportsDownloadStageThenConvertingStage()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");

        await CreateSut().RunAsync(Request(new OutputFormat.Mp3(Mp3Quality.BestVbr)), _progress, CancellationToken.None);

        lock (_reports)
        {
            Assert.Equal(new[] { PipelineStage.DownloadingAudio, PipelineStage.Converting }, _reports.Select(r => r.Stage));
            Assert.Equal(0.0, _reports[0].Fraction, precision: 6);
            Assert.Equal(2.0 / 3.0, _reports[1].Fraction, precision: 6);
        }
    }

    [Fact]
    public async Task Opus_Success_RemuxesWithTagsAndReturnsOpusLabel()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        var request = Request(new OutputFormat.Opus());

        var outcome = await CreateSut().RunAsync(request, _progress, CancellationToken.None);

        _converter.Verify(c => c.RemuxToOpusAsync(FfmpegPath, @"C:\temp\a.webm", @"C:\out\song.part.opus", request.Tags,
            request.Info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        var saved = Assert.IsType<DownloadOutcome.Saved>(outcome);
        Assert.Equal("Opus · 160 kbps", saved.FormatLabel);
    }

    [Fact]
    public async Task Opus_NoOpusStream_ReturnsFailedWithoutDownloading()
    {
        var request = Request(new OutputFormat.Opus(), SampleInfo(withOpus: false));

        var outcome = await CreateSut().RunAsync(request, _progress, CancellationToken.None);

        Assert.Equal(new DownloadOutcome.Failed("This video has no Opus audio stream to save."), outcome);
        _youtube.Verify(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
            It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        _output.Verify(o => o.Commit(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Mp4_WithAac_StreamCopiesAndReturnsMp4Label()
    {
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.mp4");
        var info = SampleInfo(withAac: true);

        var outcome = await CreateSut().RunAsync(Request(Mp4Of(info), info), _progress, CancellationToken.None);

        _converter.Verify(c => c.MuxToMp4CopyAudioAsync(FfmpegPath, @"C:\temp\v.mp4", @"C:\temp\a.mp4",
            @"C:\out\song.part.mp4", info.Duration, It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _converter.Verify(c => c.MuxToMp4Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<TimeSpan>(), It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()), Times.Never);
        var saved = Assert.IsType<DownloadOutcome.Saved>(outcome);
        Assert.Equal("MP4 · 720p", saved.FormatLabel);
    }

    [Fact]
    public async Task Mp4_WhenAudioFails_CancelsVideoAndReturnsFailedWithCause()
    {
        _temp.SetupSequence(t => t.NewTempFile(It.IsAny<string>()))
             .Returns(@"C:\temp\v.mp4").Returns(@"C:\temp\a.webm");
        // The video download runs until cancelled; the audio download fails immediately.
        var videoToken = CancellationToken.None;
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string filePath, IProgress<double>? _, CancellationToken ct) =>
                {
                    if (filePath == @"C:\temp\v.mp4")
                    {
                        videoToken = ct;
                        return Task.Delay(Timeout.Infinite, ct);
                    }
                    return Task.FromException(new InvalidOperationException("audio boom"));
                });
        var info = SampleInfo();

        var outcome = await CreateSut().RunAsync(Request(Mp4Of(info), info), _progress, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(videoToken.IsCancellationRequested);
        var failed = Assert.IsType<DownloadOutcome.Failed>(outcome);
        Assert.Equal("audio boom", failed.Message);
        Assert.IsType<InvalidOperationException>(failed.Cause);
    }

    [Fact]
    public async Task Cancelled_ReturnsCancelledAndDiscardsPart()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _youtube.Setup(s => s.DownloadStreamAsync(It.IsAny<IStreamInfo>(), It.IsAny<string>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .Returns((IStreamInfo _, string _, IProgress<double>? _, CancellationToken ct) => Task.Delay(Timeout.Infinite, ct));
        using var cts = new CancellationTokenSource();

        var run = CreateSut().RunAsync(Request(new OutputFormat.Mp3(Mp3Quality.Cbr256)), _progress, cts.Token);
        cts.Cancel();
        var outcome = await run.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsType<DownloadOutcome.Cancelled>(outcome);
        _output.Verify(o => o.Discard(@"C:\out\song.part.mp3"), Times.Once);
        _output.Verify(o => o.Commit(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CommitFails_ReturnsFailedAndDiscardsPart()
    {
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(@"C:\temp\a.webm");
        _output.Setup(o => o.Commit(It.IsAny<string>(), It.IsAny<string>())).Throws(new IOException("locked"));

        var outcome = await CreateSut().RunAsync(Request(new OutputFormat.Mp3(Mp3Quality.Cbr256)), _progress, CancellationToken.None);

        var failed = Assert.IsType<DownloadOutcome.Failed>(outcome);
        Assert.Contains("locked", failed.Message);
        _output.Verify(o => o.Discard(@"C:\out\song.part.mp3"), Times.Once);
    }

    [Fact]
    public async Task Always_DeletesTempFiles()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            _temp.Setup(t => t.NewTempFile(It.IsAny<string>())).Returns(tempFile);
            _converter.Setup(c => c.ConvertToMp3Async(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<Mp3Quality>(), It.IsAny<MediaTags?>(), It.IsAny<string?>(), It.IsAny<TimeSpan>(),
                    It.IsAny<IProgress<double>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("ffmpeg boom"));

            var outcome = await CreateSut().RunAsync(Request(new OutputFormat.Mp3(Mp3Quality.Cbr256)), _progress, CancellationToken.None);

            Assert.IsType<DownloadOutcome.Failed>(outcome);
            Assert.False(File.Exists(tempFile));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
