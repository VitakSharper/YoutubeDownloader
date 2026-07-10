namespace YoutubeDownloader.Core.Models;

/// <summary>Result of an update check.</summary>
public sealed record UpdateInfo(
    Version CurrentVersion,
    Version? LatestVersion,
    bool IsUpdateAvailable,
    string? DownloadUrl,
    string? AssetName,
    string? ReleaseUrl,
    string? ReleaseNotes);

/// <summary>Configuration for <c>GitHubUpdateService</c>. <paramref name="DownloadRoot"/> is for tests; null resolves to the user's Downloads folder.</summary>
public sealed record UpdateOptions(
    string Owner,
    string Repo,
    Version CurrentVersion,
    string? DownloadRoot = null);
