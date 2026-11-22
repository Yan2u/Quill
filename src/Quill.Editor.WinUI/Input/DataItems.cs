using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// Reads files, web addresses and image data from a data package (a drop or the clipboard, M7.3): files
/// with a path (virtual ones are left out), web links, a text that is one absolute http(s) URL, and a
/// bitmap as PNG bytes.
/// </summary>
internal static class DataItems
{
    public static bool HasItems(DataPackageView view) =>
        view.Contains(StandardDataFormats.StorageItems) || view.Contains(StandardDataFormats.WebLink)
        || view.Contains(StandardDataFormats.Bitmap) || view.Contains(StandardDataFormats.Text);

    public static async Task<DroppedItems> ReadAsync(DataPackageView view)
    {
        var paths = new List<string>();
        var urls = new List<Uri>();
        byte[]? image = null;
        try
        {
            if (view.Contains(StandardDataFormats.StorageItems))
            {
                foreach (IStorageItem item in await view.GetStorageItemsAsync())
                {
                    if (!string.IsNullOrEmpty(item.Path))
                    {
                        paths.Add(item.Path);
                    }
                }
            }

            if (view.Contains(StandardDataFormats.WebLink) && await view.GetWebLinkAsync() is { } link)
            {
                urls.Add(link);
            }
            else if (paths.Count == 0 && view.Contains(StandardDataFormats.Text)
                && (await view.GetTextAsync()).Trim() is { Length: > 0 } text && !text.Contains('\n')
                && Uri.TryCreate(text, UriKind.Absolute, out var url) && url.Scheme is "http" or "https")
            {
                urls.Add(url);
            }

            if (paths.Count == 0 && urls.Count == 0 && view.Contains(StandardDataFormats.Bitmap))
            {
                image = await PngAsync(await view.GetBitmapAsync());
            }
        }
        catch (Exception error) when (error is System.Runtime.InteropServices.COMException or UnauthorizedAccessException or InvalidOperationException)
        {
            // The source went away or holds the data: what was read so far counts.
        }

        return new DroppedItems(paths, urls, image);
    }

    /// <summary>A bitmap (often a DIB on the clipboard) encoded as PNG.</summary>
    private static async Task<byte[]?> PngAsync(RandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        using var source = await reference.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(source);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        using var target = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, target);
        encoder.SetSoftwareBitmap(bitmap);
        await encoder.FlushAsync();
        var bytes = new byte[target.Size];
        target.Seek(0);
        using var reader = new DataReader(target.GetInputStreamAt(0));
        await reader.LoadAsync((uint)bytes.Length);
        reader.ReadBytes(bytes);
        return bytes;
    }
}
