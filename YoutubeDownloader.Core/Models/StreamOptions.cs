using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.Core.Models;

public sealed record AudioStreamOption(string Codec, int BitrateKbps, string Container, IStreamInfo Source);

public sealed record VideoStreamOption(string QualityLabel, int Height, string Container, IStreamInfo Source);
