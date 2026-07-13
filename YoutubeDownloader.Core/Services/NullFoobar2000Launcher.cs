namespace YoutubeDownloader.Core.Services;

/// <summary>Inert launcher for platforms/apps without foobar2000 support (e.g. the WPF build).</summary>
public sealed class NullFoobar2000Launcher : IFoobar2000Launcher
{
    public bool IsInstalled => false;
    public void Open(string filePath) { }
}
