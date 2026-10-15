using System.Diagnostics;
using System.Net;
using YoutubeDownloader.Core.Models;
using YoutubeExplode.Exceptions;

namespace YoutubeDownloader.Core.Helpers;

/// <summary>What a failed info fetch means for the queue (018 §4 retry policy).</summary>
public abstract record FetchError
{
    private FetchError() { }

    /// <summary>Worth one retry after <see cref="QueueRetryPolicy.TransientRetryDelay"/>: 403, 5xx, timeout, network.</summary>
    public sealed record Transient(ItemState.Failed Failure) : FetchError;
    /// <summary>YouTube is rate-limiting (429): pause the whole queue.</summary>
    public sealed record RateLimited : FetchError;
    /// <summary>Retrying will not help.</summary>
    public sealed record Permanent(ItemState.Failed Failure) : FetchError;
}

/// <summary>The queue's retry numbers (018 §4).</summary>
public static class QueueRetryPolicy
{
    /// <summary>A transient fetch error is retried once: two attempts in all.</summary>
    public const int MaxFetchAttempts = 2;
    public static readonly TimeSpan TransientRetryDelay = TimeSpan.FromSeconds(2);
    /// <summary>The pauses after successive 429s; past the last one the run stops with the rest left pending.</summary>
    public static readonly IReadOnlyList<TimeSpan> RateLimitBackoff =
        [TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(120)];
}

/// <summary>What an exception or a failed download means for one queue item. Pure.</summary>
public static class QueueErrors
{
    private const string RequiresPurchaseReason = "Requires purchase";
    private const string UnavailableReason = "Unavailable (private or removed)";
    private const string UnplayableReason = "Can't be played (age-restricted or region-blocked)";
    private const string ForbiddenReason = "YouTube refused the stream (403)";
    private const string TimedOutReason = "Timed out";
    private const string DiskFullReason = "The disk is full";
    private const string NoOpusStreamReason = "No Opus audio stream";
    private const string NoMp4StreamReason = "No MP4 video stream";
    private const string FfmpegPrefix = "FFmpeg ";

    private const int ErrorDiskFull = 112;
    private const int ErrorHandleDiskFull = 39;

    /// <summary>
    /// Classifies an exception from an item's info fetch. The order matters: VideoRequiresPurchaseException
    /// and VideoUnavailableException are also VideoUnplayableExceptions. Pass an OperationCanceledException
    /// only when the caller did not cancel (a timeout).
    /// </summary>
    public static FetchError Classify(Exception ex) => ex switch
    {
        RequestLimitExceededException or HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } => new FetchError.RateLimited(),
        VideoRequiresPurchaseException => Permanent(RequiresPurchaseReason, FailureKind.RequiresPurchase),
        VideoUnavailableException => Permanent(UnavailableReason, FailureKind.Unavailable),
        VideoUnplayableException => Permanent(UnplayableReason, FailureKind.Unplayable),
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => Transient(ForbiddenReason, FailureKind.Forbidden),
        HttpRequestException { StatusCode: { } code } when code >= HttpStatusCode.InternalServerError => Transient(ServerErrorReason(code), FailureKind.Network),
        HttpRequestException { StatusCode: null } => Transient($"Network error: {ex.Message}", FailureKind.Network),
        OperationCanceledException => Transient(TimedOutReason, FailureKind.Network),
        _ => Permanent(ex.Message, FailureKind.Other)
    };

    /// <summary>True for a Windows "disk full" IOException (ERROR_DISK_FULL or ERROR_HANDLE_DISK_FULL).</summary>
    public static bool IsDiskFull(Exception? ex) => ex is IOException io && (io.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull;

    /// <summary>
    /// The item failure for a pipeline <see cref="DownloadOutcome.Failed"/> outcome (downloads are not retried):
    /// a full disk, a 403, an FFmpeg error (its summary text is kept), or anything else.
    /// </summary>
    public static ItemState.Failed ForDownloadFailure(string message, Exception? cause) => cause switch
    {
        _ when IsDiskFull(cause) => new(DiskFullReason, FailureKind.DiskFull),
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => new(ForbiddenReason, FailureKind.Forbidden),
        _ when message.StartsWith(FfmpegPrefix, StringComparison.Ordinal) => new(message, FailureKind.Conversion),
        _ => new(message, FailureKind.Other)
    };

    /// <summary>
    /// The item failure when <see cref="QueueFormatResolver.Resolve"/> found no stream for the queue's format.
    /// An MP3 always resolves, so it never gets here.
    /// </summary>
    public static ItemState.Failed NoStream(QueueFormat format) => format switch
    {
        QueueFormat.Opus => new(NoOpusStreamReason, FailureKind.NoStream),
        QueueFormat.Mp4 => new(NoMp4StreamReason, FailureKind.NoStream),
        QueueFormat.Mp3 => throw new UnreachableException("An MP3 always resolves."),
        _ => throw new UnreachableException()
    };

    private static FetchError Permanent(string reason, FailureKind kind) => new FetchError.Permanent(new ItemState.Failed(reason, kind));

    private static FetchError Transient(string reason, FailureKind kind) => new FetchError.Transient(new ItemState.Failed(reason, kind));

    private static string ServerErrorReason(HttpStatusCode code) => $"YouTube server error (HTTP {(int)code})";
}
