using System.Diagnostics;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

/// <summary>
/// Runs a playlist queue: one download at a time, with the next item's info fetched while the current one
/// downloads (018 §6, maintainer answer 5). A transient fetch error is retried once, a 429 pauses the whole
/// queue and then stops it, a bad item is skipped, a full disk stops the run (018 §4, §7).
/// One instance per run. Every await resumes on the caller's context, so state reports reach the UI thread.
/// </summary>
public sealed class PlaylistQueueRunner
{
    private const string RemovedReason = "Removed";
    private const string TimedOutReason = "The download timed out";

    private readonly IYouTubeService _youtube;
    private readonly IDownloadPipeline _pipeline;
    private readonly IRetryDelay _delay;

    private readonly object _gate = new();
    /// <summary>Video id → the linked source of the item being fetched or downloaded. Guarded by <see cref="_gate"/>.</summary>
    private readonly Dictionary<string, CancellationTokenSource> _active = new();
    /// <summary>Video ids cancelled while not active. Guarded by <see cref="_gate"/>.</summary>
    private readonly HashSet<string> _removed = new();

    public PlaylistQueueRunner(IYouTubeService youtube, IDownloadPipeline pipeline, IRetryDelay delay)
    {
        _youtube = youtube;
        _pipeline = pipeline;
        _delay = delay;
    }

    /// <summary>
    /// Runs the job and reports every item state change through <paramref name="events"/>. Never throws for an
    /// expected outcome. When it returns, no fetch or download it started is still running.
    /// </summary>
    public async Task<QueueStop> RunAsync(QueueJob job, IProgress<QueueEvent> events, CancellationToken queueToken)
    {
        var items = job.Items;
        Task<FetchResult>? ahead = null;
        var aheadIndex = -1;

        for (var i = 0; i < items.Count; i++)
        {
            var entry = items[i];
            if (queueToken.IsCancellationRequested)
                return await StopAsync(QueueStop.Cancelled, items, i, ahead, aheadIndex, events);

            Task<FetchResult> fetch;
            if (aheadIndex == i)
            {
                fetch = ahead!;
                ahead = null;
                aheadIndex = -1;
            }
            else if (IsRemoved(entry.Item))
            {
                Report(events, entry.Item, new ItemState.Skipped(RemovedReason));
                continue;
            }
            else
            {
                fetch = FetchAsync(entry.Item, queueToken, events);
            }

            var fetched = await fetch;
            if (fetched is FetchResult.RateLimited)
                fetched = await BackOffAndRefetchAsync(entry.Item, queueToken, events);

            switch (fetched)
            {
                case FetchResult.RateLimited:
                    Report(events, entry.Item, new ItemState.Pending());
                    return QueueStop.RateLimited;          // the rest stays Pending ("Resume later")
                case FetchResult.Cancelled when queueToken.IsCancellationRequested:
                    return await StopAsync(QueueStop.Cancelled, items, i, null, -1, events);
                case FetchResult.Cancelled:
                    Report(events, entry.Item, new ItemState.Cancelled());
                    continue;
                case FetchResult.Failed failed:
                    Report(events, entry.Item, failed.State);
                    continue;
                case FetchResult.Fetched ok:
                    if (IsRemoved(entry.Item))             // removed while its fetch-ahead was finishing
                    {
                        Report(events, entry.Item, new ItemState.Skipped(RemovedReason));
                        continue;
                    }
                    aheadIndex = NextIndex(items, i + 1);
                    ahead = aheadIndex >= 0 ? FetchAsync(items[aheadIndex].Item, queueToken, events) : null;
                    if (await DownloadAsync(entry, ok.Info, job, queueToken, events) is { } stop)
                        return await StopAsync(stop, items, i + 1, ahead, aheadIndex, events);
                    break;
                default:
                    throw new UnreachableException();
            }
        }
        return QueueStop.Completed;
    }

    /// <summary>
    /// Per-item cancel (018 §7). A fetching or downloading item is cancelled now and becomes Cancelled; a
    /// pending item becomes Skipped("Removed") when the run reaches it. Safe from any thread.
    /// </summary>
    public void CancelItem(string videoId)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(videoId, out var cts))
                cts.Cancel();
            else
                _removed.Add(videoId);
        }
    }

    /// <summary>Fetches one item's info: a transient error is retried once. Never throws.</summary>
    private async Task<FetchResult> FetchAsync(QueueItem item, CancellationToken queueToken, IProgress<QueueEvent> events)
    {
        using var cts = Register(item, queueToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                Report(events, item, new ItemState.Fetching(attempt));
                try
                {
                    var info = await _youtube.GetVideoInfoAsync(item.Url, cts.Token);
                    // A cancel that lands while the answer is on its way still wins: the item must not be downloaded.
                    cts.Token.ThrowIfCancellationRequested();
                    return new FetchResult.Fetched(info);
                }
                catch (Exception ex) when (!cts.IsCancellationRequested)
                {
                    switch (QueueErrors.Classify(ex))
                    {
                        case FetchError.RateLimited:
                            return new FetchResult.RateLimited();
                        case FetchError.Transient when attempt < QueueRetryPolicy.MaxFetchAttempts:
                            await _delay.WaitAsync(QueueRetryPolicy.TransientRetryDelay, cts.Token);
                            break;   // next attempt
                        case FetchError.Transient transient:
                            return new FetchResult.Failed(transient.Failure);
                        case FetchError.Permanent permanent:
                            return new FetchResult.Failed(permanent.Failure);
                        default:
                            throw new UnreachableException();
                    }
                }
            }
        }
        catch (Exception) when (cts.IsCancellationRequested)
        {
            // Once the item or the queue is cancelled, whatever the fetch throws means Cancelled.
            return new FetchResult.Cancelled();
        }
        finally
        {
            Unregister(item);
        }
    }

    /// <summary>429: the whole queue pauses (nothing else is in flight at this point, see RunAsync), then refetches.</summary>
    private async Task<FetchResult> BackOffAndRefetchAsync(QueueItem item, CancellationToken queueToken, IProgress<QueueEvent> events)
    {
        foreach (var wait in QueueRetryPolicy.RateLimitBackoff)
        {
            events.Report(new QueueEvent.RateLimitPause(wait));
            Report(events, item, new ItemState.Pending());
            try
            {
                await _delay.WaitAsync(wait, queueToken);
            }
            catch (OperationCanceledException)
            {
                return new FetchResult.Cancelled();
            }
            var result = await FetchAsync(item, queueToken, events);
            if (result is not FetchResult.RateLimited)
                return result;
        }
        return new FetchResult.RateLimited();
    }

    /// <summary>Downloads one fetched item and reports its final state; returns a stop reason, or null to go on.</summary>
    private async Task<QueueStop?> DownloadAsync(QueueJobItem entry, VideoInfo info, QueueJob job,
        CancellationToken queueToken, IProgress<QueueEvent> events)
    {
        var item = entry.Item;
        if (QueueFormatResolver.Resolve(job.Format, info) is not { } output)
        {
            Report(events, item, QueueErrors.NoStream(job.Format));
            return null;
        }

        using var cts = Register(item, queueToken);
        var progress = new DownloadingReports(events, item);
        DownloadOutcome outcome;
        try
        {
            var request = new DownloadRequest(info, output, entry.TargetPath, QueueTags.For(item, info), job.FfmpegPath);
            outcome = await _pipeline.RunAsync(request, progress, cts.Token);
        }
        finally
        {
            progress.Close();
            Unregister(item);
        }

        switch (outcome)
        {
            case DownloadOutcome.Saved saved:
                Report(events, item, new ItemState.Saved(saved.Path, saved.FormatLabel));
                return null;
            case DownloadOutcome.Cancelled when queueToken.IsCancellationRequested:
                Report(events, item, new ItemState.Cancelled());
                return QueueStop.Cancelled;
            case DownloadOutcome.Cancelled when cts.IsCancellationRequested:
                Report(events, item, new ItemState.Cancelled());
                return null;
            case DownloadOutcome.Cancelled:
                // Nobody cancelled: an HttpClient timeout surfaced as OperationCanceledException (plan 021 note).
                Report(events, item, new ItemState.Failed(TimedOutReason, FailureKind.Network));
                return null;
            case DownloadOutcome.Failed failed when QueueErrors.IsDiskFull(failed.Cause):
                Report(events, item, QueueErrors.ForDownloadFailure(failed.Message, failed.Cause));
                return QueueStop.DiskFull;
            case DownloadOutcome.Failed failed:
                Report(events, item, QueueErrors.ForDownloadFailure(failed.Message, failed.Cause));
                return null;
            default:
                throw new UnreachableException();
        }
    }

    /// <summary>
    /// Ends the run: cancels and awaits the fetch-ahead, then marks what is left. On a cancel every item from
    /// <paramref name="from"/> on becomes Cancelled (or Skipped when removed), which keeps it retryable; on any
    /// other stop only the fetch-ahead item, shown as Fetching, goes back to Pending.
    /// </summary>
    private async Task<QueueStop> StopAsync(QueueStop stop, IReadOnlyList<QueueJobItem> items, int from,
        Task<FetchResult>? ahead, int aheadIndex, IProgress<QueueEvent> events)
    {
        if (ahead is not null)
        {
            CancelIfActive(items[aheadIndex].Item.VideoId);
            await ahead;   // FetchAsync never throws; its result no longer matters
        }

        if (stop == QueueStop.Cancelled)
        {
            for (var k = from; k < items.Count; k++)
            {
                var item = items[k].Item;
                Report(events, item, IsRemoved(item) ? new ItemState.Skipped(RemovedReason) : new ItemState.Cancelled());
            }
        }
        else if (ahead is not null)
        {
            Report(events, items[aheadIndex].Item, new ItemState.Pending());
        }
        return stop;
    }

    /// <summary>The first index from <paramref name="start"/> on whose item was not removed, else -1.</summary>
    private int NextIndex(IReadOnlyList<QueueJobItem> items, int start)
    {
        for (var k = start; k < items.Count; k++)
        {
            if (!IsRemoved(items[k].Item))
                return k;
        }
        return -1;
    }

    // CancelItem and CancelIfActive call Cancel() inside the lock, and callers Unregister before the source is
    // disposed, so a disposed source is never cancelled.
    private CancellationTokenSource Register(QueueItem item, CancellationToken queueToken)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(queueToken);
        lock (_gate)
            _active[item.VideoId] = cts;
        return cts;
    }

    private void Unregister(QueueItem item)
    {
        lock (_gate)
            _active.Remove(item.VideoId);
    }

    private bool IsRemoved(QueueItem item)
    {
        lock (_gate)
            return _removed.Contains(item.VideoId);
    }

    /// <summary>Like <see cref="CancelItem"/> for an active item, but never marks the item as removed.</summary>
    private void CancelIfActive(string videoId)
    {
        lock (_gate)
        {
            if (_active.TryGetValue(videoId, out var cts))
                cts.Cancel();
        }
    }

    private static void Report(IProgress<QueueEvent> events, QueueItem item, ItemState state)
        => events.Report(new QueueEvent.ItemChanged(item, state));

    /// <summary>The result of one item's info fetch.</summary>
    private abstract record FetchResult
    {
        private FetchResult() { }

        public sealed record Fetched(VideoInfo Info) : FetchResult;
        public sealed record Failed(ItemState.Failed State) : FetchResult;
        public sealed record RateLimited : FetchResult;
        public sealed record Cancelled : FetchResult;
    }

    /// <summary>
    /// Turns pipeline reports into Downloading states until <see cref="Close"/>. A report that arrives after the
    /// outcome (a posted Progress callback, a last FFmpeg line) must not turn the item's final state back into
    /// Downloading: the rule MainViewModel applies to a single download (commit d3d4d80).
    /// </summary>
    private sealed class DownloadingReports : IProgress<PipelineProgress>
    {
        private readonly object _gate = new();
        private readonly IProgress<QueueEvent> _events;
        private readonly QueueItem _item;
        private bool _closed;

        public DownloadingReports(IProgress<QueueEvent> events, QueueItem item)
        {
            _events = events;
            _item = item;
        }

        public void Report(PipelineProgress value)
        {
            lock (_gate)
            {
                if (!_closed)
                    PlaylistQueueRunner.Report(_events, _item, new ItemState.Downloading(value.Stage, value.Fraction));
            }
        }

        /// <summary>Once this returns, no Downloading report is in flight and none will follow.</summary>
        public void Close()
        {
            lock (_gate)
                _closed = true;
        }
    }
}
