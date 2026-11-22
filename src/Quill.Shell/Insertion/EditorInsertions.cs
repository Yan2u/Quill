using Quill.Editor.Files;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;
using Quill.Settings.Insertion;

namespace Quill.Shell.Insertion;

/// <summary>
/// The app's handling of files and images in the editor (M7.3, 14-settings.md): drops follow the drop
/// settings (folders shown in the sidebar or linked, Markdown files opened or linked, other files linked or
/// left alone), images follow the image rule. A paste takes only images: image files and image data〔默认〕.
/// The window supplies what it owns: the settings, the document's path, opening folders and files, the
/// picker; reading and copying happen off the UI thread.
/// </summary>
public sealed class EditorInsertions : IEditorInsertions
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<string?> _documentPath;

    public EditorInsertions(Func<AppSettings> settings, Func<string?> documentPath)
    {
        _settings = settings;
        _documentPath = documentPath;
    }

    /// <summary>Shows a dropped folder in the sidebar.</summary>
    public Action<string>? OpenFolder { get; init; }

    /// <summary>Opens dropped Markdown files.</summary>
    public Func<IReadOnlyList<string>, Task>? OpenFiles { get; init; }

    /// <summary>Asks for image files; empty when cancelled.</summary>
    public Func<Task<IReadOnlyList<string>>>? PickImages { get; init; }

    /// <summary>Downloads online images; null leaves online images as their URLs.</summary>
    public Func<Uri, CancellationToken, Task<DownloadedImage>>? Download { get; init; } = ImageDownloader.DownloadAsync;

    /// <summary>Where images of untitled documents and from the clipboard go.</summary>
    public string ImagesFolder { get; init; } = ImageInserter.DefaultImagesFolder;

    /// <summary>The time for names of pasted image data.</summary>
    public Func<DateTimeOffset>? Now { get; init; }

    /// <summary>Raised with a message when files could not be read or copied; the host shows it.</summary>
    public event EventHandler<string>? Failed;

    public async Task<string?> InsertAsync(DroppedItems items, bool dropped)
    {
        var settings = _settings();
        try
        {
            if (!dropped)
            {
                var images = new List<InsertedItem>();
                images.AddRange(items.Paths.Where(ImageFiles.IsImage).Select(static path => new InsertedItem(InsertedKind.LocalImage, path)));
                images.AddRange(items.Urls.Where(ImageFiles.IsImage).Select(static url => new InsertedItem(InsertedKind.OnlineImage, url.OriginalString)));
                return await WriteAsync(images, items.Image, settings);
            }

            var plan = DropPlanner.Plan(items.Paths, items.Urls, settings, MarkdownFiles.IsMarkdown);
            if (plan.FolderToOpen is { } folder)
            {
                OpenFolder?.Invoke(folder);
            }

            if (plan.FilesToOpen.Count > 0 && OpenFiles is not null)
            {
                await OpenFiles(plan.FilesToOpen);
            }

            return await WriteAsync(plan.Inserted, items.Image, settings);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Failed?.Invoke(this, UiStrings.Format("insert.failed", error.Message));
            return null;
        }
    }

    public async Task<string?> PickImagesAsync()
    {
        if (PickImages is null || await PickImages() is not { Count: > 0 } paths)
        {
            return null;
        }

        return await InsertAsync(new DroppedItems(paths, []), dropped: false);
    }

    private async Task<string?> WriteAsync(IReadOnlyList<InsertedItem> items, byte[]? image, AppSettings settings)
    {
        if (items.Count == 0 && image is null)
        {
            return null;
        }

        var inserter = new ImageInserter(settings, _documentPath(), ImagesFolder, Download, Now);
        string markdown = await Task.Run(() => DropPlanner.WriteAsync(items, inserter, settings));
        if (image is not null)
        {
            string data = inserter.Markdown(await Task.Run(() => inserter.DataAsync(image)));
            markdown = markdown.Length == 0 ? data : markdown + " " + data;
        }

        return markdown.Length == 0 ? null : markdown;
    }
}
