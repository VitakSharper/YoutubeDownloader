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
            new HistoryEntry { Url = "https://youtu.be/aaa", Title = "First", Format = "MP3 · 192 kbps", FilePath = @"C:\out\first.mp3", DownloadedAt = DateTimeOffset.UnixEpoch.AddDays(2) },
            new HistoryEntry { Url = "https://youtu.be/bbb", Title = "Second", Format = "MP4 · 720p", DownloadedAt = DateTimeOffset.UnixEpoch.AddDays(1) },
        };

        store.Save(entries);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("https://youtu.be/aaa", loaded[0].Url);
        Assert.Equal("First", loaded[0].Title);
        Assert.Equal("MP3 · 192 kbps", loaded[0].Format);
        Assert.Equal(@"C:\out\first.mp3", loaded[0].FilePath);
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

    [Fact]
    public void Save_LeavesNoTmpFileBehind()
    {
        var store = new JsonHistoryStore(_dir);

        store.Save(new[] { new HistoryEntry { Url = "a" } });

        Assert.True(File.Exists(Path.Combine(_dir, "history.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "history.json.tmp")));
    }

    [Fact]
    public void Load_WhenFileCorrupt_KeepsCorruptCopyAndReturnsEmpty()
    {
        const string invalid = "{ this is not valid json ]";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "history.json"), invalid);

        var store = new JsonHistoryStore(_dir);
        Assert.Empty(store.Load());

        var copy = Assert.Single(Directory.GetFiles(_dir, "history.json.corrupt-*"));
        Assert.Equal(invalid, File.ReadAllText(copy));
    }

    [Fact]
    public void Save_AfterCorruptLoad_DoesNotOverwriteCorruptCopy()
    {
        const string invalid = "{ this is not valid json ]";
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "history.json"), invalid);
        var store = new JsonHistoryStore(_dir);
        store.Load();

        store.Save(new[] { new HistoryEntry { Url = "c" } });

        var copy = Assert.Single(Directory.GetFiles(_dir, "history.json.corrupt-*"));
        Assert.Equal(invalid, File.ReadAllText(copy));
        Assert.Equal("c", Assert.Single(store.Load()).Url);
    }

    /// <summary>
    /// A history.json as versions before plan 027b wrote it: the five properties only, indented, with
    /// System.Text.Json's default escaping ("·" escaped, the date's "+" not).
    /// </summary>
    private static readonly string OldHistoryJson = string.Join(Environment.NewLine,
        "[",
        "  {",
        "    \"Url\": \"https://www.youtube.com/watch?v=dQw4w9WgXcQ\",",
        "    \"Title\": \"Never Gonna Give You Up\",",
        "    \"Format\": \"MP3 \\u00B7 VBR V0 (~245 kbps)\",",
        "    \"FilePath\": \"C:\\\\Music\\\\Never Gonna Give You Up.mp3\",",
        "    \"DownloadedAt\": \"2026-10-01T20:15:00+02:00\"",
        "  }",
        "]");

    private static readonly string[] RunFieldKeys = ["\"VideoId\"", "\"RunId\"", "\"PlaylistId\"", "\"PlaylistTitle\"", "\"TrackIndex\""];

    [Fact]
    public void Load_OldJsonWithoutRunFields_LeavesThemNull()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "history.json"), OldHistoryJson);

        var entry = Assert.Single(new JsonHistoryStore(_dir).Load());

        Assert.Equal("https://www.youtube.com/watch?v=dQw4w9WgXcQ", entry.Url);
        Assert.Equal("Never Gonna Give You Up", entry.Title);
        Assert.Equal("MP3 · VBR V0 (~245 kbps)", entry.Format);
        Assert.Equal(@"C:\Music\Never Gonna Give You Up.mp3", entry.FilePath);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 20, 15, 0, TimeSpan.FromHours(2)), entry.DownloadedAt);
        Assert.Null(entry.VideoId);
        Assert.Null(entry.PlaylistId);
        Assert.Null(entry.PlaylistTitle);
        Assert.Null(entry.RunId);
        Assert.Null(entry.TrackIndex);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRunFields()
    {
        var store = new JsonHistoryStore(_dir);
        store.Save(new[]
        {
            new HistoryEntry
            {
                Url = "https://www.youtube.com/watch?v=dQw4w9WgXcQ", Title = "Never Gonna Give You Up", Format = "MP3 · VBR V0 (~245 kbps)",
                FilePath = @"C:\Music\'80s Sing-Alongs\007. Rick Astley - Never Gonna Give You Up.mp3", DownloadedAt = DateTimeOffset.UnixEpoch,
                VideoId = "dQw4w9WgXcQ", PlaylistId = "PL601B2E69B03FAB9D", PlaylistTitle = "'80s Sing-Alongs",
                RunId = "0123456789abcdef0123456789abcdef", TrackIndex = 7
            },
            new HistoryEntry { Url = "https://youtu.be/bbb", Title = "Single" }
        });

        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("dQw4w9WgXcQ", loaded[0].VideoId);
        Assert.Equal("PL601B2E69B03FAB9D", loaded[0].PlaylistId);
        Assert.Equal("'80s Sing-Alongs", loaded[0].PlaylistTitle);
        Assert.Equal("0123456789abcdef0123456789abcdef", loaded[0].RunId);
        Assert.Equal(7, loaded[0].TrackIndex);
        Assert.Equal(@"C:\Music\'80s Sing-Alongs\007. Rick Astley - Never Gonna Give You Up.mp3", loaded[0].FilePath);
        Assert.Null(loaded[1].VideoId);
        Assert.Null(loaded[1].RunId);
        Assert.Null(loaded[1].TrackIndex);
    }

    [Fact]
    public void Save_EntryWithoutRunFields_WritesNoNewKeys()
    {
        var path = Path.Combine(_dir, "history.json");
        var store = new JsonHistoryStore(_dir);

        store.Save(new[] { new HistoryEntry { Url = "https://youtu.be/aaa", Title = "Single", DownloadedAt = DateTimeOffset.UnixEpoch } });

        var saved = File.ReadAllText(path);
        Assert.All(RunFieldKeys, key => Assert.DoesNotContain(key, saved));

        // A pre-027b file read and written back by this build is unchanged, byte for byte.
        File.WriteAllText(path, OldHistoryJson);
        store.Save(store.Load());
        Assert.Equal(OldHistoryJson, File.ReadAllText(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
