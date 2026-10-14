using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class OutputFileServiceTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-output-tests", Guid.NewGuid().ToString("N"));

    public OutputFileServiceTests() => Directory.CreateDirectory(_dir);

    [Fact]
    public void Commit_ReplacesExistingTargetAndRemovesPart()
    {
        var target = Path.Combine(_dir, "song.mp3");
        var part = Path.Combine(_dir, "song.part.mp3");
        File.WriteAllText(target, "old");
        File.WriteAllText(part, "new");

        new OutputFileService().Commit(part, target);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public void Commit_WhenTargetMissing_MovesPart()
    {
        var target = Path.Combine(_dir, "song.mp3");
        var part = Path.Combine(_dir, "song.part.mp3");
        File.WriteAllText(part, "new");

        new OutputFileService().Commit(part, target);

        Assert.Equal("new", File.ReadAllText(target));
        Assert.False(File.Exists(part));
    }

    [Fact]
    public void Discard_DeletesPart()
    {
        var part = Path.Combine(_dir, "song.part.mp3");
        File.WriteAllText(part, "partial");

        new OutputFileService().Discard(part);

        Assert.False(File.Exists(part));
    }

    [Fact]
    public void Discard_WhenMissing_DoesNotThrow()
    {
        var ex = Record.Exception(() => new OutputFileService().Discard(Path.Combine(_dir, "missing.part.mp3")));

        Assert.Null(ex);
    }

    [Fact]
    public void Discard_WhenLocked_DoesNotThrow()
    {
        var part = Path.Combine(_dir, "song.part.mp3");
        File.WriteAllText(part, "partial");
        using var locked = new FileStream(part, FileMode.Open, FileAccess.Read, FileShare.None);

        var ex = Record.Exception(() => new OutputFileService().Discard(part));

        Assert.Null(ex);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
