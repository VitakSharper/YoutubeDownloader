using System.IO.Compression;
using System.Net;
using System.Text;
using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class GitHubUpdateServiceDownloadTests
{
    private static byte[] BuildZip(string entryName, byte[] content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry(entryName);
            using var s = entry.Open();
            s.Write(content, 0, content.Length);
        }
        return ms.ToArray();
    }

    [Fact]
    public async Task Downloads_extracts_and_returns_exe_path()
    {
        var payload = Encoding.UTF8.GetBytes("dummy-exe-bytes");
        var zipBytes = BuildZip("LiudochkaYoutubeDownloader.Avalonia.exe", payload);

        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(zipBytes)
        });

        var tempRoot = Path.Combine(Path.GetTempPath(), "yt-update-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new UpdateOptions("o", "r", new Version(1, 0, 0), tempRoot);
            var service = new GitHubUpdateService(client, options);
            var info = new UpdateInfo(new Version(1, 0, 0), new Version(1, 2, 0), true,
                "https://example.test/app.zip", "app.zip", null, null);

            var progressValues = new List<double>();
            var exePath = await service.DownloadAndExtractAsync(info, new Progress<double>(progressValues.Add));

            Assert.True(File.Exists(exePath));
            Assert.EndsWith(".exe", exePath);
            Assert.Equal(payload, await File.ReadAllBytesAsync(exePath));
            Assert.Contains(tempRoot, exePath);
            Assert.NotEmpty(progressValues);
            Assert.Equal(1.0, progressValues[^1]);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_asset_throws()
    {
        var client = StubHttpMessageHandler.Client(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var service = new GitHubUpdateService(client, new UpdateOptions("o", "r", new Version(1, 0, 0)));
        var info = new UpdateInfo(new Version(1, 0, 0), new Version(1, 2, 0), true, null, null, null, null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DownloadAndExtractAsync(info, null));
    }
}
