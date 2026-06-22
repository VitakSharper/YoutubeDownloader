namespace YoutubeDownloader.Core.Services;

/// <summary>Opens a URL in the user's default browser.</summary>
public interface ILinkOpener
{
    void Open(string url);
}
