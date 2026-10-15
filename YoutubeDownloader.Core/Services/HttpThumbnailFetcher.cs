namespace YoutubeDownloader.Core.Services;

public sealed class HttpThumbnailFetcher : IThumbnailFetcher
{
    private const string DefaultExtension = "img";

    private readonly HttpClient _http;
    private readonly ITempFileService _temp;

    public HttpThumbnailFetcher(HttpClient http, ITempFileService temp)
    {
        _http = http;
        _temp = temp;
    }

    public async Task<string?> TryDownloadAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        string? path = null;
        try
        {
            // AbsolutePath drops the query string YouTube appends (e.g. "hqdefault.jpg?sqp=…").
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.');
            path = _temp.NewTempFile(ext.Length == 0 ? DefaultExtension : ext);
            var bytes = await _http.GetByteArrayAsync(url, ct);
            await File.WriteAllBytesAsync(path, bytes, ct);
            return path;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            TryDelete(path);
            throw;
        }
        catch (Exception)
        {
            // Bad URL, HTTP error, timeout, disk error: the download goes on without a cover.
            TryDelete(path);
            return null;
        }
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
            return;
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup of a partial temp file
        }
    }
}
