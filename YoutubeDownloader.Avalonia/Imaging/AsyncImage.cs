using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace YoutubeDownloader.Avalonia.Imaging;

/// <summary>
/// Attached property that async-loads an http(s) image URL into an <see cref="Image"/>.
/// Usage: &lt;Image imaging:AsyncImage.SourceUrl="{Binding VideoInfo.ThumbnailUrl}" /&gt;
/// Fails silently (leaves the image empty) on any error.
/// </summary>
public static class AsyncImage
{
    private static readonly HttpClient Http = new();
    private static readonly ConcurrentDictionary<string, Bitmap> Cache = new();

    public static readonly AttachedProperty<string?> SourceUrlProperty =
        AvaloniaProperty.RegisterAttached<Image, string?>("SourceUrl", typeof(AsyncImage));

    public static void SetSourceUrl(Image element, string? value) => element.SetValue(SourceUrlProperty, value);
    public static string? GetSourceUrl(Image element) => element.GetValue(SourceUrlProperty);

    static AsyncImage() => SourceUrlProperty.Changed.AddClassHandler<Image>(OnChanged);

    private static async void OnChanged(Image image, AvaloniaPropertyChangedEventArgs e)
    {
        var url = e.NewValue as string;
        image.Source = null;
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (Cache.TryGetValue(url, out var cached))
            {
                image.Source = cached;
                return;
            }

            var bytes = await Http.GetByteArrayAsync(url);
            using var ms = new MemoryStream(bytes);
            var bitmap = new Bitmap(ms);
            Cache[url] = bitmap;

            // The Avalonia sync context returns us to the UI thread; guard against a stale URL.
            if (GetSourceUrl(image) == url)
                image.Source = bitmap;
        }
        catch
        {
            // ignore — leave the image empty
        }
    }
}
