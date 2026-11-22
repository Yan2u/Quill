namespace Quill.Settings.Insertion;

/// <summary>What counts as an image when files are dropped, pasted or picked (14-settings.md).</summary>
public static class ImageFiles
{
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico", ".tif", ".tiff", ".avif"];

    public static bool IsImage(string path) => Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Whether a URL names an image by the extension of its path (query and fragment ignored).</summary>
    public static bool IsImage(Uri url) => url.IsAbsoluteUri && IsImage(url.AbsolutePath);

    /// <summary>The extension for a media type ("image/png" to ".png"), or null when it is not an image type Quill knows.</summary>
    public static string? ExtensionOf(string? mediaType) => mediaType?.Split(';')[0].Trim().ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "image/bmp" => ".bmp",
        "image/webp" => ".webp",
        "image/svg+xml" => ".svg",
        "image/x-icon" or "image/vnd.microsoft.icon" => ".ico",
        "image/tiff" => ".tiff",
        "image/avif" => ".avif",
        _ => null,
    };
}
