using Microsoft.Graphics.Canvas;
using Quill.Editor.Documents;
using Quill.Editor.Services;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// Images of the document (05, Q38): sources resolved against the document's folder, decoded in the
/// background with Win2D through a <see cref="RenderCache"/> (kept by resolved path, dropped when
/// the requesting block changed meanwhile). Online images (http, https) are downloaded and decoded the
/// same way (user report, 2026-11-03: Typora shows them); one that cannot be loaded keeps its placeholder.
/// </summary>
public sealed class ImageLoader
{
    public const string Kind = "image";

    private readonly RenderCache _cache;

    public ImageLoader(RenderCache cache)
    {
        _cache = cache;
    }

    /// <summary>The folder relative image sources are resolved against; null for a document without a file.</summary>
    public string? DocumentDirectory { get; set; }

    public RenderCache Cache => _cache;

    /// <summary>The decoded image for a source shown by a block, or null while it loads or when it cannot be loaded.</summary>
    public CanvasBitmap? Get(string source, BlockId block)
    {
        if (RemoteImages.UrlOf(source) is { } url)
        {
            if (_cache.TryGet(Kind, url.AbsoluteUri, out var remote))
            {
                return remote as CanvasBitmap;
            }

            _cache.Request(block, Kind, url.AbsoluteUri, async cancellation => await RemoteImages.LoadAsync(url, cancellation));
            return null;
        }

        if (Resolve(source) is not { } path)
        {
            return null;
        }

        if (_cache.TryGet(Kind, path, out var result))
        {
            return result as CanvasBitmap;
        }

        _cache.Request(block, Kind, path, async _ => await CanvasBitmap.LoadAsync(CanvasDevice.GetSharedDevice(), path));
        return null;
    }

    /// <summary>A local file path for an image source, or null (remote, missing folder, invalid).</summary>
    public string? Resolve(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        source = source.Trim().Trim('<', '>');
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            return uri.IsFile ? uri.LocalPath : null;
        }

        if (DocumentDirectory is null)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.Combine(DocumentDirectory, Uri.UnescapeDataString(source)));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
