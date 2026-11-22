namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The host's part in inserting files and images (M7.3): the rules live in the app's settings, which the
/// editor never reads (01). The editor hands over what was dropped, pasted or asked for and inserts the
/// Markdown it gets back at the caret (a drop moves the caret to the drop point first), as one undo item.
/// </summary>
public interface IEditorInsertions
{
    /// <summary>
    /// Items dropped on the editor (<paramref name="dropped"/>) or pasted from the clipboard. Returns the
    /// Markdown to insert, or null when there is nothing to insert (the host may have opened them instead).
    /// </summary>
    Task<string?> InsertAsync(DroppedItems items, bool dropped);

    /// <summary>Format > Image > Insert local images...: asks for image files; the Markdown to insert, or null when cancelled.</summary>
    Task<string?> PickImagesAsync();
}
