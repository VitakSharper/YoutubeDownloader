using YoutubeDownloader.Core.Helpers;

namespace YoutubeDownloader.Core.Tests;

// Plain text files stand in for the exes: ExeSwapper only moves files, nothing is ever launched.
public class ExeSwapperTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-exeswapper-tests", Guid.NewGuid().ToString("N"));

    private readonly string _current;
    private readonly string _new;

    public ExeSwapperTests()
    {
        _current = Path.Combine(_dir, "app", "current.bin");
        _new = Path.Combine(_dir, "download", "new.bin");
        Directory.CreateDirectory(Path.GetDirectoryName(_current)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_new)!);
    }

    [Fact]
    public void Swap_moves_new_into_place_and_current_to_old()
    {
        File.WriteAllText(_current, "old version");
        File.WriteAllText(_new, "new version");

        ExeSwapper.Swap(_current, _new);

        Assert.Equal("new version", File.ReadAllText(_current));
        Assert.Equal("old version", File.ReadAllText(_current + ".old"));
        Assert.False(File.Exists(_new));
    }

    [Fact]
    public void Swap_with_missing_new_throws_and_restores_current()
    {
        File.WriteAllText(_current, "old version");

        Assert.Throws<FileNotFoundException>(() => ExeSwapper.Swap(_current, _new));

        Assert.Equal("old version", File.ReadAllText(_current));
        Assert.False(File.Exists(_current + ".old"));
    }

    [Fact]
    public void Undo_puts_both_files_back()
    {
        File.WriteAllText(_current, "old version");
        File.WriteAllText(_new, "new version");
        ExeSwapper.Swap(_current, _new);

        ExeSwapper.Undo(_current, _new);

        Assert.Equal("old version", File.ReadAllText(_current));
        Assert.Equal("new version", File.ReadAllText(_new));
        Assert.False(File.Exists(_current + ".old"));
    }

    [Fact]
    public void CleanupOld_deletes_existing_old()
    {
        File.WriteAllText(_current, "new version");
        File.WriteAllText(_current + ".old", "old version");

        ExeSwapper.CleanupOld(_current);

        Assert.False(File.Exists(_current + ".old"));
        Assert.Equal("new version", File.ReadAllText(_current));
    }

    [Fact]
    public void CleanupOld_with_null_or_missing_old_does_nothing()
    {
        File.WriteAllText(_current, "current");

        var ex = Record.Exception(() =>
        {
            ExeSwapper.CleanupOld(null);
            ExeSwapper.CleanupOld(_current);
        });

        Assert.Null(ex);
        Assert.Equal("current", File.ReadAllText(_current));
    }

    [Fact]
    public void CleanupOld_swallows_a_locked_old()
    {
        File.WriteAllText(_current, "new version");
        File.WriteAllText(_current + ".old", "old version");

        // The previous process is still exiting and holds its file.
        using (new FileStream(_current + ".old", FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => ExeSwapper.CleanupOld(_current));
            Assert.Null(ex);
        }

        Assert.True(File.Exists(_current + ".old"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
