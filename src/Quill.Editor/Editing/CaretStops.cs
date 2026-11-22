using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Text;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// The places the caret can stand on, in document order: positions in leaves, and the block
/// boundaries that hold a virtual paragraph. Blocks without text (rules, TOC) are passed over here;
/// vertical moves stop on them with a block caret (<see cref="BlockCarets"/>).
/// </summary>
internal static class CaretStops
{
    /// <summary>The next stop after (or before) a position, or null at the end (start) of the document.</summary>
    public static Position? Next(Document document, Position position, bool forward, bool word = false)
    {
        if (position is BoundaryPosition boundary)
        {
            return FromBoundary(document, boundary, forward);
        }

        var text = (TextPosition)position;
        var leaf = DocumentTree.Find<LeafBlock>(document, text.Block);
        int offset = Step(leaf, text.Offset, forward, word);
        return offset != text.Offset ? new TextPosition(leaf.Id, offset) : Across(document, leaf.Id, forward);
    }

    /// <summary>The first stop inside a block (the last one when going backward), or null when it has none.</summary>
    public static Position? Edge(Block block, bool forward)
    {
        return block switch
        {
            LeafBlock leaf => new TextPosition(leaf.Id, forward ? 0 : leaf.Text.Length),
            ContainerBlock container => Inside(container, forward ? 0 : container.Children.Count, forward),
            _ => null,
        };
    }

    /// <summary>
    /// A grapheme or word step inside a leaf; returns the same offset at its edge. The leading
    /// zero-width space of a paragraph is stepped over with the character after it.
    /// </summary>
    private static int Step(LeafBlock leaf, int offset, bool forward, bool word)
    {
        string text = leaf.Text;
        bool mark = leaf is Paragraph && text.Length > 0 && text[0] == Normalizer.ZeroWidthSpace;
        if (forward)
        {
            if (offset >= text.Length)
            {
                return offset;
            }

            int from = mark && offset == 0 ? 1 : offset;
            return from >= text.Length ? text.Length : word ? TextBoundaries.NextWord(text, from) : TextBoundaries.NextGrapheme(text, from);
        }

        if (offset <= 0)
        {
            return offset;
        }

        int previous = word ? TextBoundaries.PreviousWord(text, offset) : TextBoundaries.PreviousGrapheme(text, offset);
        return mark && previous <= 1 ? 0 : previous;
    }

    /// <summary>The stop after (before) a block, climbing out of containers as needed.</summary>
    private static Position? Across(Document document, BlockId blockId, bool forward)
    {
        var location = DocumentTree.Locate(document, blockId);
        for (int level = location.Ancestors.Length - 1; level >= 0; level--)
        {
            var (container, index) = location.Ancestors[level];
            var stop = Inside(container, forward ? index + 1 : index, forward);
            if (stop is not null)
            {
                return stop;
            }
        }

        return null;
    }

    private static Position? FromBoundary(Document document, BoundaryPosition boundary, bool forward)
    {
        var container = DocumentTree.Find<ContainerBlock>(document, boundary.Parent);
        var stop = forward
            ? Inside(container, boundary.Index, forward: true, skipBoundary: true)
            : Inside(container, boundary.Index, forward: false, skipBoundary: true);
        return stop ?? (container is Document ? null : Across(document, container.Id, forward));
    }

    /// <summary>
    /// The first stop in a container starting at boundary <paramref name="index"/> and going in the
    /// direction: the boundary itself when it holds a virtual paragraph, then the stops in the
    /// children one by one.
    /// </summary>
    private static Position? Inside(ContainerBlock container, int index, bool forward, bool skipBoundary = false)
    {
        var children = container.Children;
        if (forward)
        {
            for (int i = index; i <= children.Count; i++)
            {
                if (!(skipBoundary && i == index) && BlockRules.AllowsVirtualParagraph(container, i))
                {
                    return new BoundaryPosition(container.Id, i);
                }

                if (i < children.Count && Edge(children[i], forward: true) is { } stop)
                {
                    return stop;
                }
            }
        }
        else
        {
            for (int i = index; i >= 0; i--)
            {
                if (!(skipBoundary && i == index) && BlockRules.AllowsVirtualParagraph(container, i))
                {
                    return new BoundaryPosition(container.Id, i);
                }

                if (i > 0 && Edge(children[i - 1], forward: false) is { } stop)
                {
                    return stop;
                }
            }
        }

        return null;
    }
}
