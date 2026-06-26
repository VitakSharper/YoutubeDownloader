namespace YoutubeDownloader.Core.Services;

/// <summary>Reads the current text contents of the system clipboard.</summary>
public interface IClipboardService
{
	/// <summary>The clipboard's text, or <c>null</c> if it is empty, non-text, or unavailable.</summary>
	string? GetText();
}
