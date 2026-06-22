using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class JsonHistoryStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-history-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenNoFile_ReturnsEmpty()
    {
        var store = new JsonHistoryStore(_dir);
        Assert.Empty(store.Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsEntries_InOrder()
    {
        var store = new JsonHistoryStore(_dir);
        var entries = new[]
        {
            new HistoryEntry { Url = "https://youtu.be/aaa", Title = "First", Format = "MP3 · 192 kbps", DownloadedAt = DateTimeOffset.UnixEpoch.AddDays(2) },
            new HistoryEntry { Url = "https://youtu.be/bbb", Title = "Second", Format = "MP4 · 720p", DownloadedAt = DateTimeOffset.UnixEpoch.AddDays(1) },
        };

        store.Save(entries);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("https://youtu.be/aaa", loaded[0].Url);
        Assert.Equal("First", loaded[0].Title);
        Assert.Equal("MP3 · 192 kbps", loaded[0].Format);
        Assert.Equal("https://youtu.be/bbb", loaded[1].Url);
    }

    [Fact]
    public void Save_OverwritesPreviousContent()
    {
        var store = new JsonHistoryStore(_dir);
        store.Save(new[] { new HistoryEntry { Url = "a" }, new HistoryEntry { Url = "b" } });
        store.Save(new[] { new HistoryEntry { Url = "c" } });

        var loaded = store.Load();
        Assert.Single(loaded);
        Assert.Equal("c", loaded[0].Url);
    }

    [Fact]
    public void Load_WhenFileCorrupt_ReturnsEmpty()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "history.json"), "{ this is not valid json ]");

        var store = new JsonHistoryStore(_dir);
        Assert.Empty(store.Load());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
