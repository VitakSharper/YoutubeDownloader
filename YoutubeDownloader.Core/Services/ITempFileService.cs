namespace YoutubeDownloader.Core.Services;

public interface ITempFileService
{
    string NewTempFile(string extension);
}
