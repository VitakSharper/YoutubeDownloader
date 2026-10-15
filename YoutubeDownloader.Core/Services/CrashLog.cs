namespace YoutubeDownloader.Core.Services;

/// <summary>Appends unhandled exceptions to crash.log in the app data folder. Never throws.</summary>
public sealed class CrashLog
{
    private readonly string _filePath;

    public CrashLog(string directory) => _filePath = Path.Combine(directory, "crash.log");

    public string FilePath => _filePath;

    public void Write(string source, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.AppendAllText(_filePath, $"[{DateTimeOffset.Now:O}] {source}: {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never become a second failure.
        }
    }
}
