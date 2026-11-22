using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Edits shared by several behaviors, written against a <see cref="TransactionBuilder"/>.
/// </summary>
internal static class EditOperations
{
    /// <summary>Deletes a non-empty selection and leaves the caret at its start. Returns false for a caret.</summary>
    public static bool DeleteSelection(TransactionBuilder builder)
    {
        if (builder.Selection.IsCollapsed)
        {
            return false;
        }

        var (start, end) = PositionOrder.Ordered(builder.Document, builder.Selection);
        DeleteRange(builder, start, end);
        return true;
    }

    /// <summary>
    /// Deletes from <paramref name="start"/> to <paramref name="end"/> (04-commands-and-behaviors.md):
    /// the first and last blocks keep what lies outside the range, blocks in between are removed,
    /// tables and definitions only lose cell and slot text, and the two remaining halves merge when
    /// both are paragraphs or headings.
    /// </summary>
    public static void DeleteRange(TransactionBuilder builder, Position start, Position end)
    {
        if (start is TextPosition s && end is TextPosition e && s.Block == e.Block)
        {
            builder.ReplaceText(s.Block, s.Offset, e.Offset, string.Empty);
            builder.SetCaret(s with { Affinity = Affinity.Forward });
            return;
        }

        var startPath = DocumentPath.Of(builder.Document, start);
        var endPath = DocumentPath.Of(builder.Document, end);
        int common = DocumentPath.CommonLevel(startPath, endPath);

        var container = startPath[common].Container;
        var startStep = startPath[common];
        var endStep = endPath[common];

        int firstWhole = startStep.IsChild ? startStep.Index + 1 : startStep.Index;
        int lastWhole = endStep.IsChild ? endStep.Index - 1 : endStep.Index - 1;

        if (endStep.IsChild)
        {
            TrimBefore(builder, endPath, common, end);
        }

        if (lastWhole >= firstWhole)
        {
            RemoveOrClear(builder, container, firstWhole, lastWhole - firstWhole + 1);
        }

        if (startStep.IsChild)
        {
            TrimAfter(builder, startPath, common, start);
        }

        if (start is TextPosition startText && end is TextPosition endText
            && DocumentTree.Contains(builder.Document, startText.Block) && DocumentTree.Contains(builder.Document, endText.Block)
            && ((builder.Find<Block>(startText.Block) is Paragraph or Heading && builder.Find<Block>(endText.Block) is Paragraph or Heading)
                || (builder.Find<Block>(startText.Block) is SourceLine && builder.Find<Block>(endText.Block) is SourceLine)))
        {
            MergeLeaves(builder, startText.Block, endText.Block);
        }

        builder.SetCaret(start is TextPosition caret ? caret with { Affinity = Affinity.Forward } : start);
    }

    /// <summary>Appends the text of <paramref name="source"/> to <paramref name="target"/> and removes <paramref name="source"/>.</summary>
    public static void MergeLeaves(TransactionBuilder builder, BlockId target, BlockId source)
    {
        var sourceLeaf = builder.Find<LeafBlock>(source);
        var targetLeaf = builder.Find<LeafBlock>(target);
        builder.InsertText(target, targetLeaf.Text.Length, sourceLeaf.Text);
        var location = builder.Locate(source);
        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
    }

    /// <summary>
    /// Splits a leaf at <paramref name="offset"/>: the text after it moves into a new block created by
    /// <paramref name="createTail"/> and inserted right after the leaf. Returns the new block's ID.
    /// </summary>
    public static BlockId SplitLeaf(TransactionBuilder builder, BlockId leafId, int offset, Func<BlockId, string, Block> createTail)
    {
        var leaf = builder.Find<LeafBlock>(leafId);
        string tail = leaf.Text[offset..];
        builder.ReplaceText(leafId, offset, leaf.Text.Length, string.Empty);
        var newId = builder.NewId();
        var location = builder.Locate(leafId);
        builder.InsertBlocks(location.Parent!.Id, location.Index + 1, createTail(newId, tail));
        return newId;
    }

    /// <summary>Inserts a block as a sibling right after <paramref name="sibling"/>.</summary>
    public static void InsertAfter(TransactionBuilder builder, BlockId sibling, Block block)
    {
        var location = builder.Locate(sibling);
        builder.InsertBlocks(location.Parent!.Id, location.Index + 1, block);
    }

    /// <summary>Inserts a block as a sibling right before <paramref name="sibling"/>.</summary>
    public static void InsertBefore(TransactionBuilder builder, BlockId sibling, Block block)
    {
        var location = builder.Locate(sibling);
        builder.InsertBlocks(location.Parent!.Id, location.Index, block);
    }

    public static Paragraph NewParagraph(TransactionBuilder builder, string text = "") => new() { Id = builder.NewId(), Text = text };

    /// <summary>Removes everything before <paramref name="end"/> inside the child at <paramref name="level"/> of the path.</summary>
    private static void TrimBefore(TransactionBuilder builder, List<DocumentPath.Step> path, int level, Position end)
    {
        for (int i = path.Count - 1; i > level; i--)
        {
            var step = path[i];
            int count = step.Index;
            if (count > 0)
            {
                RemoveOrClear(builder, builder.Find<ContainerBlock>(step.Container.Id), 0, count);
            }
        }

        if (end is TextPosition text)
        {
            builder.ReplaceText(text.Block, 0, text.Offset, string.Empty);
        }
    }

    /// <summary>Removes everything after <paramref name="start"/> inside the child at <paramref name="level"/> of the path.</summary>
    private static void TrimAfter(TransactionBuilder builder, List<DocumentPath.Step> path, int level, Position start)
    {
        if (start is TextPosition text)
        {
            var leaf = builder.Find<LeafBlock>(text.Block);
            builder.ReplaceText(text.Block, text.Offset, leaf.Text.Length, string.Empty);
        }

        for (int i = path.Count - 1; i > level; i--)
        {
            var step = path[i];
            var container = builder.Find<ContainerBlock>(step.Container.Id);
            int from = step.IsChild ? step.Index + 1 : step.Index;
            if (from < container.Children.Count)
            {
                RemoveOrClear(builder, container, from, container.Children.Count - from);
            }
        }
    }

    /// <summary>
    /// Removes children, except inside tables and definitions, whose structure stays: there the text
    /// of the covered cells and slots is cleared (04: "跨单元格删除只清空文字").
    /// </summary>
    private static void RemoveOrClear(TransactionBuilder builder, ContainerBlock container, int index, int count)
    {
        container = builder.Find<ContainerBlock>(container.Id);
        if (container is Table or TableRow or LinkReferenceDefinition
            || (container is FootnoteDefinition && index < 2))
        {
            int structural = container is FootnoteDefinition ? Math.Min(count, 2 - index) : count;
            for (int i = index; i < index + structural; i++)
            {
                foreach (var leaf in Navigation.Leaves(container.Children[i]).ToList())
                {
                    builder.ReplaceText(leaf.Id, 0, builder.Find<LeafBlock>(leaf.Id).Text.Length, string.Empty);
                }
            }

            if (structural < count)
            {
                builder.RemoveBlocks(container.Id, index + structural, count - structural);
            }

            return;
        }

        builder.RemoveBlocks(container.Id, index, count);
    }
}
