using System.Globalization;

namespace Quill.Settings.Insertion;

/// <summary>
/// The rule for inserted images (14-settings.md, Q109): where an image is copied, and what destination is
/// inserted. Without the rule (or not applied to that kind of image) a local image is inserted as its file
/// URL and an online one as its URL. With it, the image is copied (or downloaded) into the rule's folder; a
/// file of the same name with other content gets a number after its name, one with the same content is
/// used as it is. A relative rule inserts a relative path, written as the rule is (<c>./assets/a.png</c>);
/// an absolute one inserts a file URL. An untitled document has no folder: images go to the app's images
/// folder, as file URLs. Image data (a screenshot on the clipboard) is saved under a time stamp: in the
/// app's images folder without a rule, otherwise by the rule. A failed download inserts the URL.
/// </summary>
public sealed class ImageInserter
{
    private readonly AppSettings _settings;
    private readonly string? _documentPath;
    private readonly string _imagesFolder;
    private readonly Func<Uri, CancellationToken, Task<DownloadedImage>>? _download;
    private readonly Func<DateTimeOffset> _now;

    /// <param name="documentPath">The document's file, or null for an untitled document.</param>
    /// <param name="imagesFolder">The app's images folder (<c>%LOCALAPPDATA%\Quill\images</c>).</param>
    /// <param name="download">Downloads an online image; null when there is no network access (online images keep their URL).</param>
    public ImageInserter(AppSettings settings, string? documentPath, string imagesFolder, Func<Uri, CancellationToken, Task<DownloadedImage>>? download = null, Func<DateTimeOffset>? now = null)
    {
        _settings = settings;
        _documentPath = documentPath is null ? null : Path.GetFullPath(documentPath);
        _imagesFolder = imagesFolder;
        _download = download;
        _now = now ?? (() => DateTimeOffset.Now);
    }

    /// <summary>The app's images folder: <c>%LOCALAPPDATA%\Quill\images</c>.</summary>
    public static string DefaultImagesFolder => Path.Combine(AppDataFolder.Path, "images");

    private bool HasRule => _settings.ImageRule != ImageInsertRule.None;

    /// <summary>The image as Markdown: <c>![alt](destination)</c>, escaped by the setting.</summary>
    public string Markdown(InsertedImage image) => LinkDestination.Markdown(image.AltText, image.Destination, image: true, _settings.EscapeImageUrls);

    /// <summary>A local image file.</summary>
    public Task<InsertedImage> LocalAsync(string path, CancellationToken cancellation = default)
    {
        string full = Path.GetFullPath(path);
        string alt = Path.GetFileNameWithoutExtension(full);
        if (!HasRule || !_settings.ApplyImageRuleToLocal)
        {
            return Task.FromResult(new InsertedImage(alt, LinkDestination.FileUrl(full)));
        }

        var (folder, relative) = Target();
        string stored = StoreFile(full, folder, Path.GetFileName(full));
        return Task.FromResult(new InsertedImage(Path.GetFileNameWithoutExtension(stored), Destination(stored, relative)));
    }

    /// <summary>An online image: downloaded and stored when the rule applies to online images.</summary>
    public async Task<InsertedImage> OnlineAsync(Uri url, CancellationToken cancellation = default)
    {
        string name = NameOf(url);
        string alt = Path.GetFileNameWithoutExtension(name);
        if (!HasRule || !_settings.ApplyImageRuleToOnline || _download is null)
        {
            return new InsertedImage(alt, url.OriginalString);
        }

        DownloadedImage image;
        try
        {
            image = await _download(url, cancellation).ConfigureAwait(false);
        }
        catch (Exception error) when (error is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException or UnauthorizedAccessException)
        {
            // The URL still shows the image wherever it can be reached (14-settings.md〔默认〕).
            return new InsertedImage(alt, url.OriginalString);
        }

        if (!ImageFiles.IsImage(name))
        {
            name += ImageFiles.ExtensionOf(image.MediaType) ?? ".png";
        }

        var (folder, relative) = Target();
        string stored = StoreData(image.Data, folder, name);
        return new InsertedImage(Path.GetFileNameWithoutExtension(stored), Destination(stored, relative));
    }

    /// <summary>Image data (PNG), as from a screenshot on the clipboard.</summary>
    public Task<InsertedImage> DataAsync(byte[] png, CancellationToken cancellation = default)
    {
        string name = "image-" + _now().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + ".png";
        var (folder, relative) = HasRule ? Target() : (_imagesFolder, null);
        string stored = StoreData(png, folder, name);
        return Task.FromResult(new InsertedImage(Path.GetFileNameWithoutExtension(stored), Destination(stored, relative)));
    }

    /// <summary>
    /// The folder the rule copies into, and the relative path written for it (null: insert file URLs).
    /// </summary>
    public (string Folder, string? Relative) Target()
    {
        if (_documentPath is null || Path.GetDirectoryName(_documentPath) is not { } documentFolder)
        {
            // An untitled document: a folder given as an absolute path without macros still works.
            return _settings is { ImageRule: ImageInsertRule.CopyToFolder, ImageFolder: { } absolute }
                && Path.IsPathFullyQualified(absolute) && !ImageFolderMacros.UsesMacros(absolute)
                ? (absolute, null)
                : (_imagesFolder, null);
        }

        switch (_settings.ImageRule)
        {
            case ImageInsertRule.CopyToCurrentFolder:
                return (documentFolder, string.Empty);
            case ImageInsertRule.CopyToAssets:
                return (Path.Combine(documentFolder, "assets"), "./assets");
            case ImageInsertRule.CopyToFileAssets:
                string assets = Path.GetFileNameWithoutExtension(_documentPath) + ".assets";
                return (Path.Combine(documentFolder, assets), "./" + assets);
            case ImageInsertRule.CopyToFolder:
                string expanded = ImageFolderMacros.Expand(_settings.ImageFolder?.Trim() ?? string.Empty, _documentPath);
                if (expanded.Length == 0)
                {
                    return (documentFolder, string.Empty);
                }

                return Path.IsPathFullyQualified(expanded)
                    ? (expanded, null)
                    : (Path.GetFullPath(Path.Combine(documentFolder, expanded)), expanded.Replace('\\', '/').TrimEnd('/'));
            default:
                return (documentFolder, string.Empty);
        }
    }

    private static string Destination(string stored, string? relative) => relative switch
    {
        null => LinkDestination.FileUrl(stored),
        "" => Path.GetFileName(stored),
        _ => relative + "/" + Path.GetFileName(stored),
    };

    /// <summary>A file name for an online image: the last segment of its path, without characters files cannot have.</summary>
    private static string NameOf(Uri url)
    {
        string segment = url.IsAbsoluteUri ? Uri.UnescapeDataString(url.AbsolutePath.TrimEnd('/')) : url.OriginalString;
        string name = Path.GetFileName(segment.Replace('/', Path.DirectorySeparatorChar));
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name.Trim().Length == 0 ? "image" : name.Trim();
    }

    /// <summary>Copies a file into the folder; nothing is copied when it is there already, or a file there has the same content.</summary>
    private static string StoreFile(string source, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        string target = Path.Combine(folder, name);
        if (string.Equals(Path.GetFullPath(target), source, StringComparison.OrdinalIgnoreCase))
        {
            return target;
        }

        return Store(folder, name, File.ReadAllBytes(source), path => File.Copy(source, path));
    }

    private static string StoreData(byte[] data, string folder, string name)
    {
        Directory.CreateDirectory(folder);
        return Store(folder, name, data, path => File.WriteAllBytes(path, data));
    }

    /// <summary>The first free name, <c>a.png</c>, <c>a-1.png</c>, <c>a-2.png</c>…, or a file with the same content.</summary>
    private static string Store(string folder, string name, byte[] content, Action<string> write)
    {
        string stem = Path.GetFileNameWithoutExtension(name);
        string extension = Path.GetExtension(name);
        for (int number = 0; ; number++)
        {
            string path = Path.Combine(folder, number == 0 ? name : $"{stem}-{number}{extension}");
            if (!File.Exists(path))
            {
                write(path);
                return path;
            }

            if (SameContent(path, content))
            {
                return path;
            }
        }
    }

    private static bool SameContent(string path, byte[] content)
    {
        var info = new FileInfo(path);
        return info.Length == content.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(content);
    }
}
