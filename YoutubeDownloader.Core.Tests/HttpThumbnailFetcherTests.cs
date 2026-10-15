using System.Net;
using Moq;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public sealed class HttpThumbnailFetcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "yt-thumb-test-" + Guid.NewGuid().ToString("N"));
    private readonly Mock<ITempFileService> _temp = new();

    public HttpThumbnailFetcherTests()
    {
        Directory.CreateDirectory(_dir);
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>()))
            .Returns<string>(ext => Path.Combine(_dir, $"{Guid.NewGuid():N}.{ext}"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    [Fact]
    public async Task TryDownloadAsync_Success_WritesBytesAndReturnsPath()
    {
        var image = new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3 };
        var http = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(image)
        });
        var fetcher = new HttpThumbnailFetcher(http, _temp.Object);

        var path = await fetcher.TryDownloadAsync("https://i.ytimg.com/vi_webp/abc/maxresdefault.webp?v=123", CancellationToken.None);

        Assert.NotNull(path);
        Assert.StartsWith(_dir, path);
        Assert.Equal(image, await File.ReadAllBytesAsync(path));
        _temp.Verify(t => t.NewTempFile("webp"), Times.Once); // extension from the path, query string ignored
    }

    [Fact]
    public async Task TryDownloadAsync_Http404_ReturnsNullAndLeavesNoFile()
    {
        // The temp file already exists (as a partial would); a failed fetch must remove it.
        _temp.Setup(t => t.NewTempFile(It.IsAny<string>()))
            .Returns<string>(ext =>
            {
                var p = Path.Combine(_dir, $"partial.{ext}");
                File.WriteAllBytes(p, [1, 2, 3]);
                return p;
            });
        var http = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var fetcher = new HttpThumbnailFetcher(http, _temp.Object);

        var path = await fetcher.TryDownloadAsync("https://i.ytimg.com/vi/abc/maxresdefault.jpg", CancellationToken.None);

        Assert.Null(path);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task TryDownloadAsync_NullOrEmptyUrl_ReturnsNullWithoutCallingHttp()
    {
        var calls = 0;
        var http = StubHttpMessageHandler.Client(_ =>
        {
            calls++;
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var fetcher = new HttpThumbnailFetcher(http, _temp.Object);

        Assert.Null(await fetcher.TryDownloadAsync(null, CancellationToken.None));
        Assert.Null(await fetcher.TryDownloadAsync("", CancellationToken.None));
        Assert.Null(await fetcher.TryDownloadAsync("   ", CancellationToken.None));

        Assert.Equal(0, calls);
        _temp.Verify(t => t.NewTempFile(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task TryDownloadAsync_Cancelled_ThrowsOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        var http = StubHttpMessageHandler.Client(_ =>
        {
            cts.Cancel(); // the user cancels while the thumbnail is downloading
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        var fetcher = new HttpThumbnailFetcher(http, _temp.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fetcher.TryDownloadAsync("https://i.ytimg.com/vi/abc/hqdefault.jpg", cts.Token));
        Assert.Empty(Directory.GetFiles(_dir));
    }
}
