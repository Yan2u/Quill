using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Moving onto and off a block caret (Q59, Q60, Typora): Up or Down that would pass over a TOC or a
/// thematic break stops on it,
/// and the next move leaves it in the same direction.
/// </summary>
internal static class BlockCarets
{
    /// <summary>
    /// The block caret on the first block that takes one strictly between <paramref name="from"/> and
    /// <paramref name="to"/> going in the direction, or null. Visits only the blocks in between.
    /// </summary>
    public static TextSelection? Between(Document document, Position from, Position to, bool forward)
    {
        foreach (var (parent, index) in BlocksFrom(document, from, forward))
        {
            var block = parent.Children[index];
            if (forward ? PositionOrder.Compare(document, new BoundaryPosition(parent.Id, index), to) >= 0
                        : PositionOrder.Compare(document, new BoundaryPosition(parent.Id, index + 1), to) <= 0)
            {
                return null;
            }

            if (BlockRules.TakesBlockCaret(block))
            {
                return TextSelection.BlockCaret(parent.Id, index);
            }
        }

        return null;
    }

    /// <summary>
    /// Where the caret goes when it leaves a block caret: the virtual paragraph next to the block when
    /// there is one, else the next stop; a block caret on a block in between takes precedence.
    /// Null at the edge of the document.
    /// </summary>
    public static TextSelection? Leave(Document document, TextSelection caret, bool forward)
    {
        var edge = (BoundaryPosition)(forward ? caret.Head : caret.Anchor);
        var container = DocumentTree.Find<ContainerBlock>(document, edge.Parent);
        if (BlockRules.AllowsVirtualParagraph(container, edge.Index))
        {
            return TextSelection.Caret(edge);
        }

        var stop = CaretStops.Next(document, edge, forward);
        if (stop is null)
        {
            return null;
        }

        return Between(document, edge, stop, forward) ?? TextSelection.Caret(stop);
    }

    /// <summary>The blocks after (before) a position in document order, containers before their children.</summary>
    private static IEnumerable<(ContainerBlock Parent, int Index)> BlocksFrom(Document document, Position position, bool forward)
    {
        ContainerBlock parent;
        int index;
        if (position is BoundaryPosition boundary)
        {
            parent = DocumentTree.Find<ContainerBlock>(document, boundary.Parent);
            index = forward ? boundary.Index : boundary.Index - 1;
        }
        else
        {
            var location = DocumentTree.Locate(document, ((TextPosition)position).Block);
            parent = location.Parent!;
            index = forward ? location.Index + 1 : location.Index - 1;
        }

        var stack = new Stack<(ContainerBlock, int)>();
        while (true)
        {
            if (index < 0 || index >= parent.Children.Count)
            {
                // Climb out of the container to its next (previous) sibling.
                if (stack.Count == 0)
                {
                    if (parent is Document)
                    {
                        yield break;
                    }

                    var up = DocumentTree.Locate(document, parent.Id);
                    parent = up.Parent!;
                    index = forward ? up.Index + 1 : up.Index - 1;
                }
                else
                {
                    (parent, index) = stack.Pop();
                }

                continue;
            }

            yield return (parent, index);
            if (parent.Children[index] is ContainerBlock { Children.Count: > 0 } inner)
            {
                stack.Push((parent, forward ? index + 1 : index - 1));
                parent = inner;
                index = forward ? 0 : inner.Children.Count - 1;
            }
            else
            {
                index += forward ? 1 : -1;
            }
        }
    }
}
