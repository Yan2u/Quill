using Quill.Editor.Commands;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>
/// The right click of the context menu (Q73, Q80): its entries come from <see cref="EditorMenus.Context"/>;
/// this decides what the click does to the selection first.
/// </summary>
public static class ContextMenu
{
    /// <summary>
    /// The selection change of a right click at <paramref name="hit"/>〔默认, as in Windows text boxes〕: a
    /// click inside the selection keeps it, so the menu acts on it; elsewhere the caret moves to the click.
    /// </summary>
    public static Command? SelectionFor(Position hit, EditorState state)
    {
        var selection = state.Selection;
        if (!selection.IsCollapsed && !selection.IsBlockCaret)
        {
            var (start, end) = PositionOrder.Ordered(state.Document, selection);
            if (PositionOrder.Compare(state.Document, start, hit) <= 0 && PositionOrder.Compare(state.Document, hit, end) <= 0)
            {
                return null;
            }
        }

        return selection.IsCollapsed && selection.Head.SamePlace(hit) ? null : new SetSelection(TextSelection.Caret(hit));
    }
}
