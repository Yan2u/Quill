using Microsoft.UI.Xaml;
using Quill.Editor.Commands;
using Quill.Editor.State;
using Quill.Editor.WinUI.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Quill.Editor.WinUI;

/// <summary>
/// Files and images in the editor (M7.3, 14-settings.md): files, folders and web images dropped on the editor,
/// files and image data pasted from the clipboard, and images picked with Insert local images. What to do is the
/// host's (<see cref="Insertions"/>: the settings decide); the editor inserts the Markdown it gets back at the
/// caret (a drop puts the caret at the drop point first) as one paste, so one undo item.
/// </summary>
public sealed partial class MarkdownBox
{
    /// <summary>The host's handling of dropped, pasted and picked files; without it nothing is dropped and only text pastes.</summary>
    public IEditorInsertions? Insertions { get; set; }

    /// <summary>Inserts Markdown at the caret as a paste does: parsed into blocks, one undo item.</summary>
    public void InsertMarkdown(string markdown)
    {
        if (HasDocument && !IsSourceMode && markdown.Length > 0)
        {
            Execute(new Paste(new ClipboardContent(markdown, markdown)));
        }
    }

    /// <summary>
    /// What a drop does, apart from reading the drag's data: the caret goes to the drop point (in page
    /// coordinates of the box), the host decides, the Markdown it returns is inserted. Tests call this.
    /// </summary>
    public async Task DropAsync(DroppedItems items, Point? point)
    {
        if (Insertions is null || items.IsEmpty || !HasDocument || IsSourceMode)
        {
            return;
        }

        if (point is { } at && Layout.HitTest(ToPage(at)) is { } position)
        {
            Execute(new SetSelection(TextSelection.Caret(position)));
        }

        if (await Insertions.InsertAsync(items, dropped: true) is { } markdown)
        {
            InsertMarkdown(markdown);
        }

        Focus(FocusState.Programmatic);
    }

    private void InitializeDrops()
    {
        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (Insertions is not null && !IsSourceMode && !IsReadOnly && DataItems.HasItems(e.DataView))
            {
                e.AcceptedOperation = DataPackageOperation.Copy;
                e.DragUIOverride.IsCaptionVisible = false;
            }
        };
        Drop += async (_, e) =>
        {
            if (Insertions is null || IsSourceMode || IsReadOnly)
            {
                return;
            }

            var point = e.GetPosition(this);
            var deferral = e.GetDeferral();
            DroppedItems items;
            try
            {
                items = await DataItems.ReadAsync(e.DataView);
            }
            finally
            {
                deferral.Complete();
            }

            await DropAsync(items, point);
        };
    }

    /// <summary>A paste that found no text: files or image data on the clipboard go to the host; true when there were some.</summary>
    private async Task<bool> PasteItemsAsync()
    {
        if (Insertions is null || await Clipboard.GetItemsAsync() is not { IsEmpty: false } items)
        {
            return false;
        }

        if (await Insertions.InsertAsync(items, dropped: false) is { } markdown)
        {
            InsertMarkdown(markdown);
        }

        return true;
    }
}
