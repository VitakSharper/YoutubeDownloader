namespace YoutubeDownloader.Core.Services;

public sealed class OutputFileService : IOutputFileService
{
    public void Commit(string partPath, string target) => File.Move(partPath, target, overwrite: true);

    public void Discard(string partPath)
    {
        try { if (File.Exists(partPath)) File.Delete(partPath); }
        catch { /* best-effort: may be locked or already gone */ }
    }
}
