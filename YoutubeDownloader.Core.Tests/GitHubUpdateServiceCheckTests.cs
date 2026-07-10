using System.Net;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class GitHubUpdateServiceCheckTests
{
    private const string ReleaseJson = """
    {
      "tag_name": "v1.2.0",
      "html_url": "https://github.com/VitakSharper/YoutubeDownloader/releases/tag/v1.2.0",
      "body": "Release notes",
      "assets": [
        { "name": "LiudochkaYoutubeDownloader.Avalonia-v1.2.0.zip", "browser_download_url": "https://example.test/app.zip" }
      ]
    }
    """;

    private static GitHubUpdateService Service(HttpClient client, string current = "1.0.0")
        => new(client, new UpdateOptions("VitakSharper", "YoutubeDownloader", Version.Parse(current)));

    [Fact]
    public async Task Newer_release_is_available_with_asset()
    {
        var client = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson));
        var info = await Service(client, "1.0.0").CheckAsync();

        Assert.True(info.IsUpdateAvailable);
        Assert.Equal(new Version(1, 2, 0), info.LatestVersion);
        Assert.Equal("https://example.test/app.zip", info.DownloadUrl);
        Assert.Equal("LiudochkaYoutubeDownloader.Avalonia-v1.2.0.zip", info.AssetName);
        Assert.Equal("Release notes", info.ReleaseNotes);
    }

    [Fact]
    public async Task Same_version_is_not_available()
    {
        var client = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson));
        var info = await Service(client, "1.2.0").CheckAsync();
        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task No_release_returns_null_latest()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var info = await Service(client).CheckAsync();
        Assert.Null(info.LatestVersion);
        Assert.False(info.IsUpdateAvailable);
    }

    [Fact]
    public async Task Sends_user_agent_header()
    {
        HttpRequestMessage? seen = null;
        var client = StubHttpMessageHandler.Client(req =>
        {
            seen = req;
            return StubHttpMessageHandler.Json(HttpStatusCode.OK, ReleaseJson);
        });
        await Service(client).CheckAsync();
        Assert.NotNull(seen!.Headers.UserAgent);
        Assert.NotEmpty(seen.Headers.UserAgent);
    }

    [Fact]
    public async Task Rate_limit_throws_HttpRequestException_with_status()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Service(client).CheckAsync());
        Assert.Equal(HttpStatusCode.Forbidden, ex.StatusCode);
    }

    [Fact]
    public async Task Network_failure_propagates_as_HttpRequestException()
    {
        var client = new HttpClient(new ThrowingHttpMessageHandler(new HttpRequestException("no network")));
        await Assert.ThrowsAsync<HttpRequestException>(() => Service(client).CheckAsync());
    }

    [Fact]
    public async Task Newer_release_without_zip_asset_has_no_download_url()
    {
        const string json = """
        { "tag_name": "v2.0.0", "html_url": "https://x/rel", "body": "n", "assets": [] }
        """;
        var client = StubHttpMessageHandler.Client(_ => StubHttpMessageHandler.Json(HttpStatusCode.OK, json));
        var info = await Service(client, "1.0.0").CheckAsync();
        Assert.True(info.IsUpdateAvailable);
        Assert.Null(info.DownloadUrl);
        Assert.Null(info.AssetName);
    }
}
