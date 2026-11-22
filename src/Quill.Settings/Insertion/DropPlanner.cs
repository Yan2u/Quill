namespace Quill.Settings.Insertion;

/// <summary>
/// Dropping files, folders and web images on the editor (14-settings.md, Q108). Images are inserted by the
/// image rule whatever the other settings say. A folder is opened in the sidebar or linked; a Markdown file
/// is opened or linked; any other file is linked or left alone. Only the first dropped folder can be
/// opened; Markdown files to open are opened in order (the window decides where)〔默认〕. Online URLs that do
/// not name an image are left alone〔默认〕.
/// </summary>
public static class DropPlanner
{
    /// <param name="isMarkdown">What counts as a Markdown file (Quill.Editor's <c>MarkdownFiles.IsMarkdown</c>, Q106).</param>
    public static DropPlan Plan(IReadOnlyList<string> paths, IReadOnlyList<Uri> urls, AppSettings settings, Func<string, bool> isMarkdown)
    {
        string? folder = null;
        var open = new List<string>();
        var inserted = new List<InsertedItem>();
        foreach (string path in paths)
        {
            if (Directory.Exists(path))
            {
                if (settings.DropFolder == FolderDropAction.InsertLink)
                {
                    inserted.Add(new InsertedItem(InsertedKind.Link, path));
                }
                else
                {
                    folder ??= path;
                }
            }
            else if (ImageFiles.IsImage(path))
            {
                inserted.Add(new InsertedItem(InsertedKind.LocalImage, path));
            }
            else if (isMarkdown(path))
            {
                if (settings.DropMarkdown == MarkdownDropAction.InsertLink)
                {
                    inserted.Add(new InsertedItem(InsertedKind.Link, path));
                }
                else
                {
                    open.Add(path);
                }
            }
            else if (settings.DropOtherFile == OtherFileDropAction.InsertLink && File.Exists(path))
            {
                inserted.Add(new InsertedItem(InsertedKind.Link, path));
            }
        }

        foreach (var url in urls)
        {
            if (url.IsAbsoluteUri && url.Scheme is "http" or "https" && ImageFiles.IsImage(url))
            {
                inserted.Add(new InsertedItem(InsertedKind.OnlineImage, url.OriginalString));
            }
        }

        return new DropPlan(folder, open, inserted);
    }

    /// <summary>
    /// The Markdown of the items, in order, separated by spaces〔默认〕: links by their name and file URL, images
    /// by <paramref name="images"/>. Escaping follows "escape image URLs" for both (06).
    /// </summary>
    public static async Task<string> WriteAsync(IReadOnlyList<InsertedItem> items, ImageInserter images, AppSettings settings, CancellationToken cancellation = default)
    {
        var parts = new List<string>(items.Count);
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case InsertedKind.Link:
                    string full = Path.GetFullPath(item.Source);
                    string name = Path.GetFileName(full.TrimEnd('\\', '/'));
                    parts.Add(LinkDestination.Markdown(name.Length > 0 ? name : full, LinkDestination.FileUrl(full), image: false, settings.EscapeImageUrls));
                    break;
                case InsertedKind.LocalImage:
                    parts.Add(images.Markdown(await images.LocalAsync(item.Source, cancellation).ConfigureAwait(false)));
                    break;
                case InsertedKind.OnlineImage:
                    parts.Add(images.Markdown(await images.OnlineAsync(new Uri(item.Source), cancellation).ConfigureAwait(false)));
                    break;
            }
        }

        return string.Join(" ", parts);
    }
}
