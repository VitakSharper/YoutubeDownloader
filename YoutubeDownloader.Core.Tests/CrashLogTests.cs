using YoutubeDownloader.Core.Services;

namespace YoutubeDownloader.Core.Tests;

public class CrashLogTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ydl-crashlog-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Write_AppendsBothEntries()
    {
        var log = new CrashLog(_dir);

        log.Write("UI", new InvalidOperationException("first boom"));
        log.Write("Task", new IOException("second boom"));

        var text = File.ReadAllText(log.FilePath);
        Assert.Contains("UI", text);
        Assert.Contains("first boom", text);
        Assert.Contains("Task", text);
        Assert.Contains("second boom", text);
    }

    [Fact]
    public void Write_WhenDirectoryIsAFile_DoesNotThrow()
    {
        Directory.CreateDirectory(_dir);
        var notADirectory = Path.Combine(_dir, "blocker");
        File.WriteAllText(notADirectory, "I am a file");
        var log = new CrashLog(notADirectory);

        var ex = Record.Exception(() => log.Write("UI", new InvalidOperationException("boom")));

        Assert.Null(ex);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
