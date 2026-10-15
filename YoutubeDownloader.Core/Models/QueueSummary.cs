namespace YoutubeDownloader.Core.Models;

/// <summary>End-of-run counts over the items of one run (018 §9).</summary>
/// <param name="NotStarted">Items still pending, fetching or downloading when the run ended.</param>
public sealed record QueueSummary(int Total, int Saved, int Failed, int Skipped, int Cancelled, int NotStarted)
{
    private const string Separator = " · ";

    public static QueueSummary From(IReadOnlyCollection<ItemState> states) => new(
        Total: states.Count,
        Saved: states.Count(s => s is ItemState.Saved),
        Failed: states.Count(s => s is ItemState.Failed),
        Skipped: states.Count(s => s is ItemState.Skipped),
        Cancelled: states.Count(s => s is ItemState.Cancelled),
        NotStarted: states.Count(s => s is ItemState.Pending or ItemState.Fetching or ItemState.Downloading));

    /// <summary>"84 of 86 saved · 1 failed · 1 skipped"; zero counts are left out.</summary>
    public string Describe() => string.Join(Separator, new[]
    {
        $"{Saved} of {Total} saved",
        Part(Failed, "failed"),
        Part(Skipped, "skipped"),
        Part(Cancelled, "cancelled"),
        Part(NotStarted, "not started")
    }.OfType<string>());

    private static string? Part(int count, string label) => count == 0 ? null : $"{count} {label}";
}
