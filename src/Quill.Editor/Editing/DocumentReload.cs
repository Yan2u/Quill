using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Replaces the document with the file read again (M6.4): one step replacing the root, so the reload is
/// one undo item and its inverse restores the document (and its format) as it was before.
/// </summary>
internal static class DocumentReload
{
    public static Transaction Build(EditorState state, Document reloaded, IBlockIdSource ids, DateTimeOffset now)
    {
        var builder = new TransactionBuilder(state, ids);
        var document = reloaded with { Id = state.Document.Id };
        builder.ReplaceBlock(document);
        builder.SetCaret(Equivalent(state, builder.Document));
        return builder.Build(TransactionKind.Edit, now) ?? throw new InvalidOperationException("A reload always builds a transaction.");
    }

    /// <summary>The caret's place in the new document: the leaf with the same index in document order, its offset clamped.</summary>
    private static Position Equivalent(EditorState state, Document after)
    {
        var leaves = Navigation.Leaves(after).ToList();
        if (leaves.Count == 0)
        {
            return new BoundaryPosition(after.Id, 0);
        }

        int index = 0, offset = 0;
        if (state.Selection.Head is TextPosition head)
        {
            index = Math.Max(0, Navigation.Leaves(state.Document).ToList().FindIndex(leaf => leaf.Id == head.Block));
            offset = head.Offset;
        }

        var leaf = leaves[Math.Min(index, leaves.Count - 1)];
        return new TextPosition(leaf.Id, Math.Min(offset, leaf.Text.Length));
    }
}
