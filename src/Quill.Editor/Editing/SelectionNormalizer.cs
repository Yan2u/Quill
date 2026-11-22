using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Text;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Keeps selections valid (03-editor-state.md): positions in existing leaves on grapheme boundaries,
/// the caret never right after a leading zero-width space, boundary carets only where a virtual
/// paragraph may appear, and cross-block selections never ending inside an atomic unit.
/// </summary>
internal static class SelectionNormalizer
{
    public static TextSelection Normalize(Document document, TextSelection selection)
    {
        if (selection.IsBlockCaret)
        {
            return IsValidBlockCaret(document, selection) ? selection : Normalize(document, selection with { IsBlockCaret = false });
        }

        bool collapsed = selection.IsCollapsed;
        var head = NormalizePosition(document, selection.Head, collapsed);
        var anchor = collapsed ? head : NormalizePosition(document, selection.Anchor, collapsed: false);
        if (!collapsed && !anchor.SamePlace(head))
        {
            (anchor, head) = ExtendOverAtomicUnits(document, anchor, head);
        }

        var result = selection with { Anchor = anchor, Head = head };
        return result == selection ? selection : result;
    }

    /// <summary>A block caret spans exactly one block that takes it (Q59).</summary>
    private static bool IsValidBlockCaret(Document document, TextSelection selection) =>
        selection is { Anchor: BoundaryPosition before, Head: BoundaryPosition after }
        && before.Parent == after.Parent
        && after.Index == before.Index + 1
        && DocumentTree.TryFind(document, before.Parent) is ContainerBlock container
        && before.Index >= 0 && before.Index < container.Children.Count
        && BlockRules.TakesBlockCaret(container.Children[before.Index]);

    public static Position NormalizePosition(Document document, Position position, bool collapsed)
    {
        switch (position)
        {
            case TextPosition text when DocumentTree.Contains(document, text.Block):
            {
                var block = DocumentTree.Find(document, text.Block);
                if (block is LeafBlock leaf)
                {
                    int offset = TextBoundaries.SnapToGrapheme(leaf.Text, text.Offset);
                    if (offset == 1 && leaf is Paragraph && leaf.Text[0] == Normalizer.ZeroWidthSpace)
                    {
                        offset = 0;
                    }

                    return offset == text.Offset ? text : text with { Offset = offset };
                }

                return CaretNear(document, Navigation.First(document, block), collapsed);
            }

            case BoundaryPosition boundary when DocumentTree.Contains(document, boundary.Parent)
                && DocumentTree.Find(document, boundary.Parent) is ContainerBlock container:
            {
                int index = Math.Clamp(boundary.Index, 0, container.Children.Count);
                var clamped = index == boundary.Index ? boundary : boundary with { Index = index };
                return CaretNear(document, clamped, collapsed);
            }

            default:
                return CaretNear(document, Navigation.First(document, document), collapsed);
        }
    }

    /// <summary>
    /// A caret at a boundary that cannot hold a virtual paragraph moves to the end of the block
    /// before it, or else to the start of the block after it.
    /// </summary>
    private static Position CaretNear(Document document, Position position, bool collapsed)
    {
        if (position is not BoundaryPosition boundary || !collapsed)
        {
            return position;
        }

        var container = DocumentTree.Find<ContainerBlock>(document, boundary.Parent);
        if (BlockRules.AllowsVirtualParagraph(container, boundary.Index))
        {
            return boundary;
        }

        if (boundary.Index > 0 && Navigation.Last(document, container.Children[boundary.Index - 1]) is TextPosition before)
        {
            return before;
        }

        if (boundary.Index < container.Children.Count && Navigation.First(document, container.Children[boundary.Index]) is TextPosition after)
        {
            return after;
        }

        var first = Navigation.Leaves(document).FirstOrDefault();
        return first is not null ? new TextPosition(first.Id, 0) : new BoundaryPosition(document.Id, 0);
    }

    /// <summary>Code blocks and math blocks are selected whole or not at all by a selection that leaves them.</summary>
    private static (Position Anchor, Position Head) ExtendOverAtomicUnits(Document document, Position anchor, Position head)
    {
        bool forward = PositionOrder.Compare(document, anchor, head) <= 0;
        var start = forward ? anchor : head;
        var end = forward ? head : anchor;
        if (start is TextPosition s && end is TextPosition e && s.Block == e.Block)
        {
            return (anchor, head);
        }

        start = OutsideAtomic(document, start, before: true);
        end = OutsideAtomic(document, end, before: false);
        return forward ? (start, end) : (end, start);
    }

    private static Position OutsideAtomic(Document document, Position position, bool before)
    {
        if (position is TextPosition text && DocumentTree.Find(document, text.Block) is (CodeBlock or MathBlock or CodeLanguage) and var block)
        {
            // A code block counts whole with its language (Q91).
            var location = DocumentTree.Locate(document, (CodeFence.Of(document, block) ?? block).Id);
            return new BoundaryPosition(location.Parent!.Id, before ? location.Index : location.Index + 1);
        }

        return position;
    }
}
