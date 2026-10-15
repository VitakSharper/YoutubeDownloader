using System.Net;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;
using YoutubeExplode.Exceptions;
using YoutubeExplode.Videos;

namespace YoutubeDownloader.Core.Tests;

public class QueueErrorsTests
{
    private const int DiskFullHResult = unchecked((int)0x80070070);         // ERROR_DISK_FULL (112)
    private const int HandleDiskFullHResult = unchecked((int)0x80070027);   // ERROR_HANDLE_DISK_FULL (39)

    private static FetchError Permanent(string reason, FailureKind kind) => new FetchError.Permanent(new ItemState.Failed(reason, kind));

    private static FetchError Transient(string reason, FailureKind kind) => new FetchError.Transient(new ItemState.Failed(reason, kind));

    [Fact]
    public void RequestLimitExceeded_IsRateLimited()
    {
        var error = QueueErrors.Classify(new RequestLimitExceededException("Too many requests"));

        Assert.IsType<FetchError.RateLimited>(error);
    }

    [Fact]
    public void Http429_IsRateLimited()
    {
        var error = QueueErrors.Classify(new HttpRequestException("Too Many Requests", null, HttpStatusCode.TooManyRequests));

        Assert.IsType<FetchError.RateLimited>(error);
    }

    [Fact]
    public void RequiresPurchase_IsPermanentRequiresPurchase()
    {
        var ex = new VideoRequiresPurchaseException("Paid video", VideoId.Parse("dQw4w9WgXcQ"));
        Assert.IsAssignableFrom<VideoUnplayableException>(ex);   // the order trap: it must not read as "unplayable"

        var error = QueueErrors.Classify(ex);

        Assert.Equal(Permanent("Requires purchase", FailureKind.RequiresPurchase), error);
    }

    [Fact]
    public void Unavailable_IsPermanentUnavailable()
    {
        var error = QueueErrors.Classify(new VideoUnavailableException("Video is unavailable"));

        Assert.Equal(Permanent("Unavailable (private or removed)", FailureKind.Unavailable), error);
    }

    [Fact]
    public void Unplayable_IsPermanentUnplayable()
    {
        var error = QueueErrors.Classify(new VideoUnplayableException("Sign in to confirm your age"));

        Assert.Equal(Permanent("Can't be played (age-restricted or region-blocked)", FailureKind.Unplayable), error);
    }

    [Fact]
    public void Http403_IsTransientForbidden()
    {
        var error = QueueErrors.Classify(new HttpRequestException("Forbidden", null, HttpStatusCode.Forbidden));

        Assert.Equal(Transient("YouTube refused the stream (403)", FailureKind.Forbidden), error);
    }

    [Theory]
    [InlineData(500)]
    [InlineData(503)]
    public void ServerError_IsTransientNetwork(int status)
    {
        var error = QueueErrors.Classify(new HttpRequestException("Server error", null, (HttpStatusCode)status));

        Assert.Equal(Transient($"YouTube server error (HTTP {status})", FailureKind.Network), error);
    }

    [Fact]
    public void Timeout_IsTransient()
    {
        var error = QueueErrors.Classify(new TaskCanceledException("t", new TimeoutException()));

        Assert.Equal(Transient("Timed out", FailureKind.Network), error);
    }

    [Fact]
    public void NetworkErrorWithoutStatus_IsTransient()
    {
        var error = QueueErrors.Classify(new HttpRequestException("No such host is known."));

        Assert.Equal(Transient("Network error: No such host is known.", FailureKind.Network), error);
    }

    [Fact]
    public void UnknownError_IsPermanentOther()
    {
        var error = QueueErrors.Classify(new InvalidOperationException("Something unexpected"));

        Assert.Equal(Permanent("Something unexpected", FailureKind.Other), error);
    }

    [Theory]
    [InlineData(DiskFullHResult)]
    [InlineData(HandleDiskFullHResult)]
    public void IsDiskFull_RecognisesWindowsCodes(int hresult)
    {
        Assert.True(QueueErrors.IsDiskFull(new IOException("x", hresult)));
    }

    [Fact]
    public void IsDiskFull_OtherIOExceptionOrNull_IsFalse()
    {
        Assert.False(QueueErrors.IsDiskFull(new IOException("x")));
        Assert.False(QueueErrors.IsDiskFull(null));
    }

    [Fact]
    public void ForDownloadFailure_FfmpegMessage_IsConversionWithItsText()
    {
        const string message = "FFmpeg exited with code 1: Invalid data found when processing input";

        var failure = QueueErrors.ForDownloadFailure(message, new InvalidOperationException(message));

        Assert.Equal(new ItemState.Failed(message, FailureKind.Conversion), failure);
    }

    [Fact]
    public void ForDownloadFailure_Http403Cause_IsForbidden()
    {
        var cause = new HttpRequestException("Response status code does not indicate success: 403 (Forbidden).", null, HttpStatusCode.Forbidden);

        var failure = QueueErrors.ForDownloadFailure(cause.Message, cause);

        Assert.Equal(new ItemState.Failed("YouTube refused the stream (403)", FailureKind.Forbidden), failure);
    }

    [Fact]
    public void ForDownloadFailure_DiskFull_IsDiskFull()
    {
        var cause = new IOException("There is not enough space on the disk.", DiskFullHResult);

        var failure = QueueErrors.ForDownloadFailure(cause.Message, cause);

        Assert.Equal(new ItemState.Failed("The disk is full", FailureKind.DiskFull), failure);
    }
}
