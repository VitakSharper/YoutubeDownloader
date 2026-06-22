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

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
