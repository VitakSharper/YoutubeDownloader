using System.Diagnostics;
using System.Net;
using Moq;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.Tests;

/// <summary>
/// Every fake either completes at once or waits on a gate or its own token: no test depends on timing. The retry
/// delay is a mock that completes at once and records what was asked. The fetch-ahead and the current download
/// can still report from different threads (xUnit 2's async context posts continuations to its own threads), so
/// the event list is always read and written under a lock, and every awaited run has a 5 s guard so a bug fails
/// instead of hanging.
/// </summary>
public class PlaylistQueueRunnerTests
{
    private const string Mp3Label = "MP3 · VBR V0 (~245 kbps)";
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(5);

    private readonly Mock<IYouTubeService> _youtube = new();
    private readonly Mock<IDownloadPipeline> _pipeline = new();
    private readonly Mock<IRetryDelay> _delay = new();
    private readonly List<TimeSpan> _delays = new();
    private readonly List<QueueEvent> _events = new();

    public PlaylistQueueRunnerTests()
    {
        _delay.Setup(d => d.WaitAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
              .Callback<TimeSpan, CancellationToken>((delay, _) => { lock (_delays) _delays.Add(delay); })
              .Returns(Task.CompletedTask);
        _youtube.Setup(s => s.GetVideoInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string url, CancellationToken _) => Info(Item(IndexOf(url))));
        SetupPipeline((r, _, _) => Task.FromResult(Saved(r)));
    }

    private static QueueItem Item(int index) => new(
        $"vid{index:D8}", $"https://www.youtube.com/watch?v=vid{index:D8}", $"Title {index}", "Channel",
        TimeSpan.FromMinutes(3), index, null);

    private static string Target(int index) => $@"C:\out\{index:D3}. Channel - Title {index}.mp3";

    private static QueueJob Job(params int[] indexes) => new(
        indexes.Select(i => new QueueJobItem(Item(i), Target(i))).ToArray(),
        new QueueFormat.Mp3(Mp3Quality.BestVbr),
        @"C:\ffmpeg\ffmpeg.exe");

    /// <summary>The item index from a URL built by <see cref="Item"/>.</summary>
    private static int IndexOf(string url) => int.Parse(url[^8..]);

    private static VideoInfo Info(QueueItem item, bool withOpus = true) => new(
        SourceUrl: item.Url,
        Title: item.Title,
        Author: item.Author,
        Duration: TimeSpan.FromMinutes(3),
        ThumbnailUrl: "",
        BestAudio: new AudioStreamOption("opus", 128, "webm", Source: null!),
        VideoOptions: [new VideoStreamOption("720p", 720, "mp4", Source: null!)],
        BestOpusAudio: withOpus ? new AudioStreamOption("opus", 160, "webm", Source: null!) : null);

    private static DownloadOutcome Saved(DownloadRequest request) => new DownloadOutcome.Saved(request.TargetPath, Mp3Label);

    private static ItemState.Saved SavedState(int index) => new(Target(index), Mp3Label);

    private static HttpRequestException Forbidden() =>
        new("Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden);

    /// <summary>A download that runs until its token is cancelled, then answers Cancelled as DownloadPipeline does.</summary>
    private static async Task<DownloadOutcome> RunUntilCancelledAsync(CancellationToken ct)
    {
        try
        {
            await new TaskCompletionSource().Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return new DownloadOutcome.Cancelled();
        }
        throw new UnreachableException("The gate never opens.");
    }

    private void SetupPipeline(Func<DownloadRequest, IProgress<PipelineProgress>, CancellationToken, Task<DownloadOutcome>> run)
        => _pipeline.Setup(p => p.RunAsync(It.IsAny<DownloadRequest>(), It.IsAny<IProgress<PipelineProgress>>(), It.IsAny<CancellationToken>()))
                    .Returns(run);

    private PlaylistQueueRunner CreateRunner() => new(_youtube.Object, _pipeline.Object, _delay.Object);

    private Task<QueueStop> Start(PlaylistQueueRunner runner, QueueJob job, CancellationToken ct = default)
        => runner.RunAsync(job, new InlineProgress<QueueEvent>(e => { lock (_events) _events.Add(e); }), ct);

    private Task<QueueStop> RunToEndAsync(PlaylistQueueRunner runner, QueueJob job) => Start(runner, job).WaitAsync(Guard);

    private QueueEvent[] Events()
    {
        lock (_events)
            return _events.ToArray();
    }

    private ItemState[] States(QueueItem item)
        => Events().OfType<QueueEvent.ItemChanged>().Where(e => e.Item == item).Select(e => e.State).ToArray();

    private ItemState? FinalState(QueueItem item) => States(item).LastOrDefault();

    private TimeSpan[] Pauses() => Events().OfType<QueueEvent.RateLimitPause>().Select(p => p.Wait).ToArray();

    private TimeSpan[] Delays()
    {
        lock (_delays)
            return _delays.ToArray();
    }

    private DownloadRequest[] Requests() => _pipeline.Invocations.Select(i => (DownloadRequest)i.Arguments[0]).ToArray();

    private int FetchCount(QueueItem item) => _youtube.Invocations.Count(i =>
        i.Method.Name == nameof(IYouTubeService.GetVideoInfoAsync) && Equals(i.Arguments[0], item.Url));

    [Fact]
    public async Task RunsItemsInOrder_AndSavesEach()
    {
        var stop = await RunToEndAsync(CreateRunner(), Job(1, 2, 3));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new[] { Target(1), Target(2), Target(3) }, Requests().Select(r => r.TargetPath));
        Assert.Equal(SavedState(1), FinalState(Item(1)));
        Assert.Equal(SavedState(2), FinalState(Item(2)));
        Assert.Equal(SavedState(3), FinalState(Item(3)));
    }

    [Fact]
    public async Task FetchesNextItemWhileCurrentOneDownloads()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetupPipeline(async (r, _, ct) =>
        {
            if (r.TargetPath == Target(1))
                await gate.Task.WaitAsync(ct);
            return Saved(r);
        });
        var item2 = Item(2);

        var run = Start(CreateRunner(), Job(1, 2));

        Assert.True(SpinWait.SpinUntil(() => FetchCount(item2) == 1, Guard));
        Assert.False(run.IsCompleted);
        Assert.Equal(new[] { Target(1) }, Requests().Select(r => r.TargetPath));   // still one download at a time
        gate.SetResult();
        Assert.Equal(QueueStop.Completed, await run.WaitAsync(Guard));
        Assert.Equal(SavedState(2), FinalState(item2));
    }

    [Fact]
    public async Task FetchFailure_SkipsTheItemAndContinues()
    {
        var item2 = Item(2);
        _youtube.Setup(s => s.GetVideoInfoAsync(item2.Url, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new VideoUnavailableException("Video is unavailable"));

        var stop = await RunToEndAsync(CreateRunner(), Job(1, 2, 3));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Failed("Unavailable (private or removed)", FailureKind.Unavailable), FinalState(item2));
        Assert.DoesNotContain(Target(2), Requests().Select(r => r.TargetPath));
        Assert.Equal(SavedState(3), FinalState(Item(3)));
    }

    [Fact]
    public async Task Forbidden_IsRetriedOnceAfterTwoSeconds()
    {
        var item = Item(1);
        _youtube.SetupSequence(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>()))
                .ThrowsAsync(Forbidden())
                .ReturnsAsync(Info(item));

        var stop = await RunToEndAsync(CreateRunner(), Job(1));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, Delays());
        Assert.Equal(new ItemState[] { new ItemState.Fetching(1), new ItemState.Fetching(2), SavedState(1) }, States(item));
    }

    [Fact]
    public async Task ForbiddenTwice_FailsTheItem()
    {
        var item = Item(1);
        _youtube.Setup(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>())).ThrowsAsync(Forbidden());

        var stop = await RunToEndAsync(CreateRunner(), Job(1));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Failed("YouTube refused the stream (403)", FailureKind.Forbidden), FinalState(item));
        Assert.Equal(2, FetchCount(item));
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, Delays());
        Assert.Empty(Requests());
    }

    [Fact]
    public async Task RateLimited_BacksOff30_60_120_ThenStops()
    {
        var item1 = Item(1);
        _youtube.Setup(s => s.GetVideoInfoAsync(item1.Url, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestLimitExceededException("Too many requests"));

        var stop = await RunToEndAsync(CreateRunner(), Job(1, 2));

        Assert.Equal(QueueStop.RateLimited, stop);
        TimeSpan[] backoff = [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];
        Assert.Equal(backoff, Delays());
        Assert.Equal(backoff, Pauses());
        Assert.Equal(4, FetchCount(item1));   // the first try, then one after each pause
        Assert.Equal(new ItemState.Pending(), FinalState(item1));
        Assert.Equal(0, FetchCount(Item(2)));
        Assert.Empty(States(Item(2)));
        Assert.Empty(Requests());
    }

    [Fact]
    public async Task RateLimitedOnce_ThenRecovers()
    {
        var item = Item(1);
        _youtube.SetupSequence(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new RequestLimitExceededException("Too many requests"))
                .ReturnsAsync(Info(item));

        var stop = await RunToEndAsync(CreateRunner(), Job(1));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, Delays());
        Assert.Equal(new[] { TimeSpan.FromSeconds(30) }, Pauses());
        Assert.Equal(SavedState(1), FinalState(item));
    }

    [Fact]
    public async Task CancelItem_Pending_IsSkippedAsRemoved()
    {
        var item3 = Item(3);
        var runner = CreateRunner();
        runner.CancelItem(item3.VideoId);

        var stop = await RunToEndAsync(runner, Job(1, 2, 3));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState[] { new ItemState.Skipped("Removed") }, States(item3));
        Assert.Equal(0, FetchCount(item3));
        Assert.Equal(SavedState(1), FinalState(Item(1)));
        Assert.Equal(SavedState(2), FinalState(Item(2)));
    }

    [Fact]
    public async Task CancelItem_Downloading_CancelsOnlyThatItem()
    {
        SetupPipeline((r, _, ct) => r.TargetPath == Target(1) ? RunUntilCancelledAsync(ct) : Task.FromResult(Saved(r)));
        var item1 = Item(1);
        var runner = CreateRunner();

        var run = Start(runner, Job(1, 2));
        Assert.True(SpinWait.SpinUntil(() => Requests().Length == 1, Guard));   // item 1 is downloading
        runner.CancelItem(item1.VideoId);
        var stop = await run.WaitAsync(Guard);

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Cancelled(), FinalState(item1));
        Assert.Equal(SavedState(2), FinalState(Item(2)));
    }

    [Fact]
    public async Task CancelAll_CancelsActiveAndFetchAhead_AndMarksTheRestCancelled()
    {
        var item2 = Item(2);
        var item2Token = CancellationToken.None;
        _youtube.Setup(s => s.GetVideoInfoAsync(item2.Url, It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) =>
                {
                    item2Token = ct;
                    return new TaskCompletionSource<VideoInfo>().Task.WaitAsync(ct);
                });
        SetupPipeline((r, _, ct) => r.TargetPath == Target(1) ? RunUntilCancelledAsync(ct) : Task.FromResult(Saved(r)));
        using var queue = new CancellationTokenSource();

        var run = Start(CreateRunner(), Job(1, 2, 3), queue.Token);
        Assert.True(SpinWait.SpinUntil(() => Requests().Length == 1 && FetchCount(item2) == 1, Guard));
        queue.Cancel();
        var stop = await run.WaitAsync(Guard);

        Assert.Equal(QueueStop.Cancelled, stop);
        Assert.Equal(new ItemState.Cancelled(), FinalState(Item(1)));
        Assert.Equal(new ItemState.Cancelled(), FinalState(item2));
        Assert.Equal(new ItemState.Cancelled(), FinalState(Item(3)));
        Assert.True(item2Token.IsCancellationRequested);
        Assert.Single(Requests());
        Assert.Equal(0, FetchCount(Item(3)));
    }

    [Fact]
    public async Task NoOpusStream_FailsWithoutCallingThePipeline()
    {
        var item1 = Item(1);
        _youtube.Setup(s => s.GetVideoInfoAsync(item1.Url, It.IsAny<CancellationToken>())).ReturnsAsync(Info(item1, withOpus: false));
        var job = Job(1, 2) with { Format = new QueueFormat.Opus() };

        var stop = await RunToEndAsync(CreateRunner(), job);

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Failed("No Opus audio stream", FailureKind.NoStream), FinalState(item1));
        var request = Assert.Single(Requests());
        Assert.Equal(Target(2), request.TargetPath);
        Assert.IsType<OutputFormat.Opus>(request.Format);
        Assert.IsType<ItemState.Saved>(FinalState(Item(2)));
    }

    [Fact]
    public async Task VideoFormat_PassesTheHeightCappedOption()
    {
        var item = Item(1);
        var info = Info(item) with
        {
            VideoOptions =
            [
                new VideoStreamOption("1080p", 1080, "mp4", Source: null!),
                new VideoStreamOption("720p", 720, "mp4", Source: null!),
                new VideoStreamOption("360p", 360, "mp4", Source: null!)
            ]
        };
        _youtube.Setup(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>())).ReturnsAsync(info);
        var job = Job(1) with { Format = new QueueFormat.Mp4(720) };

        await RunToEndAsync(CreateRunner(), job);

        var mp4 = Assert.IsType<OutputFormat.Mp4>(Assert.Single(Requests()).Format);
        Assert.Equal(720, mp4.Video.Height);
    }

    [Fact]
    public async Task DiskFull_StopsAndPutsTheFetchAheadBackToPending()
    {
        var diskFull = new IOException("full", unchecked((int)0x80070070));   // ERROR_DISK_FULL
        SetupPipeline((r, _, _) => Task.FromResult<DownloadOutcome>(
            r.TargetPath == Target(1) ? new DownloadOutcome.Failed("x", diskFull) : Saved(r)));

        var stop = await RunToEndAsync(CreateRunner(), Job(1, 2, 3));

        Assert.Equal(QueueStop.DiskFull, stop);
        Assert.Equal(new ItemState.Failed("The disk is full", FailureKind.DiskFull), FinalState(Item(1)));
        Assert.Equal(new ItemState[] { new ItemState.Fetching(1), new ItemState.Pending() }, States(Item(2)));
        Assert.Empty(States(Item(3)));
        Assert.Single(Requests());
    }

    [Fact]
    public async Task UnrequestedCancel_IsReportedAsTimeout()
    {
        SetupPipeline((r, _, _) => Task.FromResult<DownloadOutcome>(
            r.TargetPath == Target(1) ? new DownloadOutcome.Cancelled() : Saved(r)));

        var stop = await RunToEndAsync(CreateRunner(), Job(1, 2));

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Failed("The download timed out", FailureKind.Network), FinalState(Item(1)));
        Assert.Equal(SavedState(2), FinalState(Item(2)));
    }

    [Fact]
    public async Task PipelineProgress_BecomesDownloadingState_AndUsesTheItemUrl()
    {
        var item = Item(1);
        // The fetched info carries another form of the URL: the tags must still get the item's normalized URL.
        _youtube.Setup(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Info(item) with { SourceUrl = $"https://youtu.be/{item.VideoId}" });
        SetupPipeline((r, progress, _) =>
        {
            progress.Report(new PipelineProgress(PipelineStage.Converting, 0.8));
            return Task.FromResult(Saved(r));
        });

        await RunToEndAsync(CreateRunner(), Job(1));

        Assert.Contains(new QueueEvent.ItemChanged(item, new ItemState.Downloading(PipelineStage.Converting, 0.8)), Events());
        _youtube.Verify(s => s.GetVideoInfoAsync(item.Url, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(item.Url, Assert.Single(Requests()).Tags.SourceUrl);
    }

    // Executor additions (beyond the plan's fifteen): the two hardening rules in PlaylistQueueRunner.

    [Fact]
    public async Task ProgressReportArrivingAfterTheOutcome_DoesNotReplaceTheFinalState()
    {
        IProgress<PipelineProgress>? captured = null;
        SetupPipeline((r, progress, _) =>
        {
            captured = progress;
            return Task.FromResult(Saved(r));
        });

        await RunToEndAsync(CreateRunner(), Job(1));
        captured!.Report(new PipelineProgress(PipelineStage.Converting, 0.9));

        Assert.Equal(SavedState(1), FinalState(Item(1)));
    }

    [Fact]
    public async Task CancelItem_FetchAnsweringAfterTheCancel_DoesNotDownloadTheItem()
    {
        var item1 = Item(1);
        // The fetch answers only once its token is cancelled, like a response already on its way.
        _youtube.Setup(s => s.GetVideoInfoAsync(item1.Url, It.IsAny<CancellationToken>()))
                .Returns((string _, CancellationToken ct) =>
                {
                    var answer = new TaskCompletionSource<VideoInfo>(TaskCreationOptions.RunContinuationsAsynchronously);
                    ct.Register(() => answer.SetResult(Info(item1)));
                    return answer.Task;
                });
        var runner = CreateRunner();

        var run = Start(runner, Job(1, 2));
        Assert.True(SpinWait.SpinUntil(() => FetchCount(item1) == 1, Guard));
        runner.CancelItem(item1.VideoId);
        var stop = await run.WaitAsync(Guard);

        Assert.Equal(QueueStop.Completed, stop);
        Assert.Equal(new ItemState.Cancelled(), FinalState(item1));
        Assert.Equal(new[] { Target(2) }, Requests().Select(r => r.TargetPath));
        Assert.Equal(SavedState(2), FinalState(Item(2)));
    }
}
