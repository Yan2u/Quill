using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Selection rules that need the inline syntax (03-editor-state.md):
/// <list type="bullet">
/// <item>Markers follow the selection (Typora, Q18): when an element is collapsed (not active when the
/// selection forms) and the selection covers all of its content, the selection grows over its markers on
/// both sides. An expanded element is selected exactly as the user chose; partial cover never grows.</item>
/// <item>Images are atomic units: a selection that leaves its block never ends inside an image.</item>
/// </list>
/// </summary>
internal static class InlineSelection
{
    /// <param name="active">The active set when the selection forms (frozen while the pointer is down).</param>
    public static TextSelection Extend(Document document, TextSelection selection, ActiveSet active)
    {
        if (selection.IsCollapsed)
        {
            return selection;
        }

        bool forward = PositionOrder.Compare(document, selection.Anchor, selection.Head) <= 0;
        var start = forward ? selection.Anchor : selection.Head;
        var end = forward ? selection.Head : selection.Anchor;
        bool crossBlock = !(start is TextPosition s && end is TextPosition e && s.Block == e.Block);
        // Growing over inner markers can complete the cover of an outer element (***a***): repeat until stable.
        Position newStart = start;
        Position newEnd = end;
        while (true)
        {
            var grownStart = Grow(document, newStart, newEnd, active, crossBlock, atStart: true);
            var grownEnd = Grow(document, newEnd, grownStart, active, crossBlock, atStart: false);
            if (grownStart == newStart && grownEnd == newEnd)
            {
                break;
            }

            (newStart, newEnd) = (grownStart, grownEnd);
        }

        if (newStart == start && newEnd == end)
        {
            return selection;
        }

        return forward ? new TextSelection(newStart, newEnd) : new TextSelection(newEnd, newStart);
    }

    private static Position Grow(Document document, Position position, Position other, ActiveSet active, bool crossBlock, bool atStart)
    {
        if (position is not TextPosition text || DocumentTree.TryFind(document, text.Block) is not TextLeafBlock leaf)
        {
            return position;
        }

        var tree = InlineTrees.Get(document, leaf);
        int offset = text.Offset;
        int otherOffset = other is TextPosition o && o.Block == leaf.Id ? o.Offset : atStart ? int.MaxValue : int.MinValue;
        foreach (var node in tree.Descendants())
        {
            if (crossBlock && node.Kind == InlineKind.Image && node.Range.ContainsStrictly(offset))
            {
                offset = atStart ? Math.Min(offset, node.Range.Start) : Math.Max(offset, node.Range.End);
                continue;
            }

            if (!Activation.CanActivate(node.Kind) || IsActive(active, leaf.Id, node))
            {
                continue;
            }

            if (atStart
                && node.Opening.Length > 0
                && text.Offset > node.Range.Start && text.Offset <= node.Content.Start
                && otherOffset >= node.Content.End)
            {
                offset = Math.Min(offset, node.Range.Start);
            }
            else if (!atStart
                && node.Closing.Length > 0
                && text.Offset >= node.Content.End && text.Offset < node.Range.End
                && otherOffset <= node.Content.Start)
            {
                offset = Math.Max(offset, node.Range.End);
            }
        }

        return offset == text.Offset ? position : text with { Offset = offset };
    }

    private static bool IsActive(ActiveSet active, BlockId leaf, InlineNode node) =>
        active.FocusedBlock == leaf && active.ActiveNodes.Any(candidate => candidate.Kind == node.Kind && candidate.Range == node.Range);
}
