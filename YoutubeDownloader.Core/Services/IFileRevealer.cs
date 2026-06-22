namespace YoutubeDownloader.Core.Services;

/// <summary>Reveals a downloaded file in the OS file manager (selecting it when possible).</summary>
public interface IFileRevealer
{
    void RevealInFolder(string filePath);
}
