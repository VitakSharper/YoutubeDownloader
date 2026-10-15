namespace YoutubeDownloader.Core.Models;

/// <summary>Why a queue item failed; the queue's retry policy and its summary read it.</summary>
public enum FailureKind { Unavailable, Unplayable, RequiresPurchase, Forbidden, Network, NoStream, Conversion, DiskFull, Other }
