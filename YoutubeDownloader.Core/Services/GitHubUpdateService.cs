using System.Net;
using System.Text.Json;
using YoutubeDownloader.Core.Helpers;
using YoutubeDownloader.Core.Models;

namespace YoutubeDownloader.Core.Services;

public sealed class GitHubUpdateService : IUpdateService
{
    private const string UserAgent = "LiudochkaYoutubeDownloader";

    private readonly HttpClient _http;
    private readonly UpdateOptions _options;

    public GitHubUpdateService(HttpClient http, UpdateOptions options)
    {
        _http = http;
        _options = options;
    }

    public async Task<UpdateInfo> CheckAsync(CancellationToken ct = default)
    {
        var url = $"https://api.github.com/repos/{_options.Owner}/{_options.Repo}/releases/latest";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd(UserAgent);
        req.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var res = await _http.SendAsync(req, ct);
        if (res.StatusCode == HttpStatusCode.NotFound)
            return new UpdateInfo(_options.CurrentVersion, null, false, null, null, null, null);

        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadAsStringAsync(ct);
        return Parse(json);
    }

    private UpdateInfo Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
        var releaseUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() : null;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;

        string? downloadUrl = null, assetName = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (name is not null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    assetName = name;
                    downloadUrl = a.TryGetProperty("browser_download_url", out var d) ? d.GetString() : null;
                    break;
                }
            }
        }

        Version? latest = ReleaseVersion.TryParseTag(tag, out var v) ? v : null;
        var available = latest is not null && latest > _options.CurrentVersion;
        return new UpdateInfo(_options.CurrentVersion, latest, available, downloadUrl, assetName, releaseUrl, notes);
    }

    public Task<string> DownloadAndExtractAsync(UpdateInfo info, IProgress<double>? progress, CancellationToken ct = default)
        => throw new NotImplementedException(); // Task 3
}
