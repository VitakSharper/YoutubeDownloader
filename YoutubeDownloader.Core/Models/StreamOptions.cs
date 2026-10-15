using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Models;

/// <param name="IsOriginalLanguage">
/// <c>true</c> when the manifest flags this track as the video's original language, <c>false</c> for another
/// language (e.g. an AI-dubbed track), <c>null</c> when the manifest gave no language data.
/// </param>
public sealed record AudioStreamOption(string Codec, int BitrateKbps, string Container, IStreamInfo Source, bool? IsOriginalLanguage = null);

public sealed record VideoStreamOption(string QualityLabel, int Height, string Container, IStreamInfo Source);
