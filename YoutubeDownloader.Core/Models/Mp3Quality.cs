namespace YoutubeDownloader.Core.Models;

/// <summary>
/// An MP3 encoding preset — either LAME VBR (a quality level) or CBR (a fixed kbps).
/// </summary>
public sealed class Mp3Quality
{
    private Mp3Quality(string label, string historyLabel, bool isVbr, int vbrQuality, int approxKbps)
    {
        Label = label;
        HistoryLabel = historyLabel;
        IsVbr = isVbr;
        VbrQuality = vbrQuality;
        ApproxKbps = approxKbps;
    }

    /// <summary>Dropdown text, e.g. "Best (VBR V0)" or "320 kbps".</summary>
    public string Label { get; }

    /// <summary>Descriptor used in history: "MP3 · {HistoryLabel}".</summary>
    public string HistoryLabel { get; }

    /// <summary>True → LAME VBR (-q:a); false → CBR (-b:a).</summary>
    public bool IsVbr { get; }

    /// <summary>LAME VBR quality level (0 = best). Meaningful only when <see cref="IsVbr"/>.</summary>
    public int VbrQuality { get; }

    /// <summary>Target kbps for CBR; the approximate average for VBR. Drives the upscale warning.</summary>
    public int ApproxKbps { get; }

    public static readonly Mp3Quality BestVbr = new("Best (VBR V0)", "VBR V0 (~245 kbps)", isVbr: true, vbrQuality: 0, approxKbps: 245);
    public static readonly Mp3Quality Cbr320 = new("320 kbps", "320 kbps", isVbr: false, vbrQuality: 0, approxKbps: 320);
    public static readonly Mp3Quality Cbr256 = new("256 kbps", "256 kbps", isVbr: false, vbrQuality: 0, approxKbps: 256);
    public static readonly Mp3Quality Cbr192 = new("192 kbps", "192 kbps", isVbr: false, vbrQuality: 0, approxKbps: 192);
    public static readonly Mp3Quality Cbr128 = new("128 kbps", "128 kbps", isVbr: false, vbrQuality: 0, approxKbps: 128);

    /// <summary>All presets in dropdown order (best first).</summary>
    public static readonly IReadOnlyList<Mp3Quality> All = new[] { BestVbr, Cbr320, Cbr256, Cbr192, Cbr128 };

    public override string ToString() => Label;
}
