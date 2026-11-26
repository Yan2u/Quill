using Quill.Editor.Commands;
using Windows.ApplicationModel.DataTransfer;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// Where copies go and pastes come from (04: plain text plus Quill's private Markdown format).
/// The editor talks to this interface; tests use <see cref="MemoryClipboard"/> so they never touch the
/// user's clipboard (08).
/// </summary>
public interface IClipboardService
{
    Task SetAsync(ClipboardContent content);

    Task<ClipboardContent?> GetAsync();

    /// <summary>Whether a paste would find text, files or an image (enables the context menu's paste entries).</summary>
    bool CanPaste { get; }

    /// <summary>Files and image data on the clipboard (M7.3), for a paste that found no text; null when there are none.</summary>
    Task<DroppedItems?> GetItemsAsync() => Task.FromResult<DroppedItems?>(null);
}

/// <summary>A clipboard in memory, for tests and previews.</summary>
public sealed class MemoryClipboard : IClipboardService
{
    public ClipboardContent? Content { get; set; }

    public Task SetAsync(ClipboardContent content)
    {
        Content = content;
        return Task.CompletedTask;
    }

    /// <summary>Files or image data on the clipboard, for tests of pasted images.</summary>
    public DroppedItems? Items { get; set; }

    public Task<ClipboardContent?> GetAsync() => Task.FromResult(Content);

    public Task<DroppedItems?> GetItemsAsync() => Task.FromResult(Items);

    public bool CanPaste => Content is not null || Items is not null;
}

/// <summary>
/// The system clipboard: plain text for every program, Quill's private Markdown format, and raw
/// CF_HTML ("HTML Format") for interoperable table copy/paste (04, Q20, Q119–Q123). All HTML
/// encoding and parsing stays in <c>Quill.Editor</c>; this service never calls
/// <c>HtmlFormatHelper.GetStaticFragment</c>.
/// </summary>
public sealed class SystemClipboard : IClipboardService
{
    /// <summary>The private format: the Markdown source of the copied selection.</summary>
    public const string MarkdownFormat = "Quill.Markdown";

    public Task SetAsync(ClipboardContent content)
    {
        var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
        package.SetText(content.PlainText);
        if (content.Markdown is { } markdown)
        {
            package.SetData(MarkdownFormat, markdown);
        }

        if (content.Html is { Length: > 0 } html)
        {
            package.SetHtmlFormat(html);
        }

        Clipboard.SetContent(package);
        Clipboard.Flush();
        return Task.CompletedTask;
    }

    public bool CanPaste
    {
        get
        {
            try
            {
                var view = Clipboard.GetContent();
                return view.Contains(StandardDataFormats.Text) || view.Contains(MarkdownFormat)
                    || view.Contains(StandardDataFormats.Html)
                    || view.Contains(StandardDataFormats.StorageItems) || view.Contains(StandardDataFormats.Bitmap);
            }
            catch (Exception)
            {
                // Another program holds the clipboard open: offer the paste, which then finds nothing or retries.
                return true;
            }
        }
    }

    public async Task<ClipboardContent?> GetAsync()
    {
        var view = Clipboard.GetContent();
        string? markdown = view.Contains(MarkdownFormat) ? await view.GetDataAsync(MarkdownFormat) as string : null;
        string? text = view.Contains(StandardDataFormats.Text) ? await view.GetTextAsync() : null;
        string? html = null;
        if (view.Contains(StandardDataFormats.Html))
        {
            try
            {
                html = await view.GetHtmlFormatAsync();
            }
            catch (Exception)
            {
                // Some clipboard owners advertise CF_HTML without readable data; fall back to text.
            }
        }

        return markdown is null && text is null && html is null
            ? null
            : new ClipboardContent(markdown, text ?? markdown ?? string.Empty, html);
    }

    public async Task<DroppedItems?> GetItemsAsync()
    {
        var items = await DataItems.ReadAsync(Clipboard.GetContent());
        return items.IsEmpty ? null : items;
    }
}
