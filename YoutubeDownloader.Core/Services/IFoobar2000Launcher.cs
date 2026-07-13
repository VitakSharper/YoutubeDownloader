namespace YoutubeDownloader.Core.Services;

/// <summary>Detects and launches foobar2000 to play a downloaded file.</summary>
public interface IFoobar2000Launcher
{
    /// <summary>True when a foobar2000 executable was located on this machine.</summary>
    bool IsInstalled { get; }

    /// <summary>Open the given file in foobar2000. No-op when not installed.</summary>
    void Open(string filePath);
}
