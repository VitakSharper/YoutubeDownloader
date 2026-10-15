using YoutubeDownloader.Core.Models;
using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class JsonSettingsStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-settings-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Load_WhenNoFile_ReturnsDefaults()
    {
        var store = new JsonSettingsStore(_dir);
        Assert.True(store.Load().AlwaysOnTop); // default
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAlwaysOnTop()
    {
        var store = new JsonSettingsStore(_dir);

        store.Save(new AppSettings { AlwaysOnTop = false });

        Assert.False(store.Load().AlwaysOnTop);
    }

    [Fact]
    public void Load_WhenCorrupt_ReturnsDefaults()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "not json");

        Assert.True(new JsonSettingsStore(_dir).Load().AlwaysOnTop);
    }

    [Fact]
    public void Save_LeavesNoTmpFileBehind()
    {
        var store = new JsonSettingsStore(_dir);

        store.Save(new AppSettings { AlwaysOnTop = false });

        Assert.True(File.Exists(Path.Combine(_dir, "settings.json")));
        Assert.False(File.Exists(Path.Combine(_dir, "settings.json.tmp")));
    }

    [Fact]
    public void Load_WhenCorrupt_KeepsCorruptCopy()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "not json");

        new JsonSettingsStore(_dir).Load();

        Assert.False(File.Exists(Path.Combine(_dir, "settings.json")));
        var copy = Assert.Single(Directory.GetFiles(_dir, "settings.json.corrupt-*"));
        Assert.Equal("not json", File.ReadAllText(copy));
    }

    [Fact]
    public void Load_OldFileWithoutNewFields_KeepsValuesAndDefaultsTheRest()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "settings.json"),
            """{ "AlwaysOnTop": false, "LastSaveFolder": "C:\\Music" }""");

        var loaded = new JsonSettingsStore(_dir).Load();

        Assert.False(loaded.AlwaysOnTop);
        Assert.Equal(@"C:\Music", loaded.LastSaveFolder);
        Assert.Equal("", loaded.LastMode);
        Assert.Equal("", loaded.LastMp3Quality);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsModeAndQuality()
    {
        var store = new JsonSettingsStore(_dir);

        store.Save(new AppSettings { LastMode = "AudioOpus", LastMp3Quality = "cbr-256" });

        var loaded = store.Load();
        Assert.Equal("AudioOpus", loaded.LastMode);
        Assert.Equal("cbr-256", loaded.LastMp3Quality);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
