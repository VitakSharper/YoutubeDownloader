namespace YoutubeDownloader.Core.Services;

public sealed class TempFileService : ITempFileService
{
    public string NewTempFile(string extension)
    {
        var ext = extension.TrimStart('.');
        var dir = Path.Combine(Path.GetTempPath(), "YoutubeDownloader");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"{Guid.NewGuid():N}.{ext}");
    }
}
