using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-atomic-tests", Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void WriteAllText_ReplacesContentAndLeavesNoTmp()
    {
        var path = Path.Combine(_dir, "x.json");

        AtomicFile.WriteAllText(path, "a");
        AtomicFile.WriteAllText(path, "b");

        Assert.Equal("b", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Quarantine_MovesFileAside()
    {
        var path = Path.Combine(_dir, "x.json");
        File.WriteAllText(path, "broken");

        AtomicFile.Quarantine(path);

        Assert.False(File.Exists(path));
        Assert.Single(Directory.GetFiles(_dir, "x.json.corrupt-*"));
    }

    [Fact]
    public void Quarantine_WhenMissing_DoesNotThrow()
    {
        var path = Path.Combine(_dir, "missing.json");

        var ex = Record.Exception(() => AtomicFile.Quarantine(path));

        Assert.Null(ex);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
