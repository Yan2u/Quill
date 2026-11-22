using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.State;

namespace Quill.Editor.Transactions;

/// <summary>
/// A reversible change to the document (04-commands-and-behaviors.md). Applying a step yields its
/// inverse; <see cref="Map"/> carries a position across the change.
/// </summary>
public abstract record Step
{
    /// <summary>Applies the step, stamping changed nodes with <paramref name="revision"/>.</summary>
    internal abstract Document Apply(Document document, long revision, out Step inverse);

    /// <summary>Where a position of <paramref name="before"/> lies in <paramref name="after"/>.</summary>
    internal abstract Position Map(Position position, Document before, Document after);

    /// <summary>The block whose content or children the step changes.</summary>
    internal abstract BlockId Target { get; }
}

/// <summary>
/// Replaces the source range [<see cref="From"/>, <see cref="To"/>) of a leaf with <see cref="Text"/>.
/// <see cref="Prefixes"/> are the prefix records of the lines the inserted text starts, and
/// <see cref="EditedLinePrefix"/> the record of the line the range starts in (both set by inverse
/// steps, so that undo restores the original syntax exactly). Without the latter, a line that was
/// blank and is edited loses its record: a blank line may be written with less indentation than its
/// container needs for text (06-markdown-io.md), so its prefix is generated again.
/// </summary>
public sealed record ReplaceTextStep(
    BlockId Block,
    int From,
    int To,
    string Text,
    ImmutableArray<LinePrefix> Prefixes = default,
    LinePrefix? EditedLinePrefix = null) : Step
{
    internal override BlockId Target => Block;

    internal override Document Apply(Document document, long revision, out Step inverse)
    {
        var leaf = DocumentTree.Find<LeafBlock>(document, Block);
        if (From < 0 || To < From || To > leaf.Text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(To), $"Range {From}..{To} is outside the {leaf.Text.Length} characters of {Block}.");
        }

        var (prefixes, removed) = LinePrefixSplice.Splice(leaf.Text, leaf.LinePrefixes, From, To, Text, Prefixes);
        string text = string.Concat(leaf.Text.AsSpan(0, From), Text, leaf.Text.AsSpan(To));
        int line = leaf.Text.AsSpan(0, From).Count('\n');
        var before = line < leaf.LinePrefixes.Length ? leaf.LinePrefixes[line] : LinePrefix.None;
        var edited = EditedLinePrefix
            ?? (!before.IsNone && LinePrefixSplice.IsBlankLine(leaf.Text, From) ? LinePrefix.None : before);
        if (edited != before)
        {
            prefixes = LinePrefixSplice.Set(prefixes, line, edited);
        }

        // The inverse restores the record exactly, whatever the rule above decides on the way back.
        inverse = new ReplaceTextStep(Block, From, From + Text.Length, leaf.Text[From..To], removed, before);
        var updated = leaf with { Text = text, LinePrefixes = prefixes };
        return DocumentTree.Replace(document, updated, revision);
    }

    internal override Position Map(Position position, Document before, Document after)
    {
        if (position is not TextPosition text || text.Block != Block)
        {
            return position;
        }

        int delta = Text.Length - (To - From);
        if (text.Offset < From || (text.Offset == From && From < To) || (text.Offset == From && text.Affinity == Affinity.Backward))
        {
            return text;
        }

        if (text.Offset > To)
        {
            return text with { Offset = text.Offset + delta };
        }

        // Inside the replaced range, or at a pure insertion point with forward affinity.
        return text with { Offset = text.Affinity == Affinity.Backward ? From : From + Text.Length };
    }
}

/// <summary>Inserts <see cref="Blocks"/> as children of <see cref="Parent"/> before child <see cref="Index"/>.</summary>
public sealed record InsertBlocksStep(BlockId Parent, int Index, ImmutableArray<Block> Blocks) : Step
{
    internal override BlockId Target => Parent;

    internal override Document Apply(Document document, long revision, out Step inverse)
    {
        inverse = new RemoveBlocksStep(Parent, Index, Blocks.Length);
        return DocumentTree.Insert(document, Parent, Index, Blocks, revision);
    }

    internal override Position Map(Position position, Document before, Document after)
    {
        return position is BoundaryPosition boundary && boundary.Parent == Parent && boundary.Index > Index
            ? boundary with { Index = boundary.Index + Blocks.Length }
            : position;
    }
}

/// <summary>Removes <see cref="Count"/> children of <see cref="Parent"/> starting at child <see cref="Index"/>.</summary>
public sealed record RemoveBlocksStep(BlockId Parent, int Index, int Count) : Step
{
    internal override BlockId Target => Parent;

    internal override Document Apply(Document document, long revision, out Step inverse)
    {
        var result = DocumentTree.Remove(document, Parent, Index, Count, out var removed, revision);
        inverse = new InsertBlocksStep(Parent, Index, removed);
        return result;
    }

    internal override Position Map(Position position, Document before, Document after)
    {
        if (position is BoundaryPosition boundary && boundary.Parent == Parent)
        {
            if (boundary.Index <= Index)
            {
                return boundary;
            }

            return boundary with { Index = boundary.Index >= Index + Count ? boundary.Index - Count : Index };
        }

        // Positions inside the removed blocks move to where the blocks were.
        BlockId owner = position is TextPosition text ? text.Block : ((BoundaryPosition)position).Parent;
        return DocumentTree.Contains(after, owner) ? position : new BoundaryPosition(Parent, Index);
    }
}

/// <summary>Replaces the block with <see cref="Replacement"/>'s ID (type or property changes, restructured subtrees).</summary>
public sealed record ReplaceBlockStep(Block Replacement) : Step
{
    internal override BlockId Target => Replacement.Id;

    internal override Document Apply(Document document, long revision, out Step inverse)
    {
        inverse = new ReplaceBlockStep(DocumentTree.Find(document, Replacement.Id));
        return DocumentTree.Replace(document, Replacement, revision);
    }

    internal override Position Map(Position position, Document before, Document after)
    {
        BlockId owner = position is TextPosition text ? text.Block : ((BoundaryPosition)position).Parent;
        if (DocumentTree.Contains(after, owner))
        {
            if (position is TextPosition kept && DocumentTree.Find(after, owner) is LeafBlock leaf && kept.Offset > leaf.Text.Length)
            {
                return kept with { Offset = leaf.Text.Length };
            }

            if (position is BoundaryPosition boundary && DocumentTree.Find(after, owner) is ContainerBlock container && boundary.Index > container.Children.Count)
            {
                return boundary with { Index = container.Children.Count };
            }

            return position;
        }

        if (Replacement is LeafBlock)
        {
            return new TextPosition(Replacement.Id, 0);
        }

        var location = DocumentTree.Locate(after, Replacement.Id);

        // The root replaced whole (a reload): the start of the document.
        return location.Parent is { } parent ? new BoundaryPosition(parent.Id, location.Index) : new BoundaryPosition(Replacement.Id, 0);
    }
}
