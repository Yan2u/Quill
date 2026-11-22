using Quill.Editor.Documents;

namespace Quill.Editor.Transactions;

/// <summary>
/// Restores the invariants of 02-document-model.md after a transaction's steps, looking only at the
/// blocks the steps touched (and their ancestors), by appending further steps.
/// </summary>
internal static class Normalizer
{
    /// <summary>U+200B, inserted before a paragraph whose first line starts with a tab or four spaces (Q28, Q34).</summary>
    public const char ZeroWidthSpace = '​';

    public static void Normalize(TransactionBuilder builder)
    {
        RemoveEmptyContainers(builder);
        NormalizeTaskItems(builder);
        if (builder.Document.Children.IsEmpty)
        {
            builder.InsertBlocks(builder.Document.Id, 0, new Paragraph { Id = builder.NewId(), Text = string.Empty });
        }

        foreach (var id in builder.Touched.ToList())
        {
            if (!DocumentTree.Contains(builder.Document, id) || builder.Find<Block>(id) is not LeafBlock leaf)
            {
                continue;
            }

            switch (leaf)
            {
                case Heading heading:
                    if (heading.Text.Contains('\n'))
                    {
                        ReplaceAll(builder, heading, "\n", " ");
                    }

                    EscapeClosingSequence(builder, builder.Find<Heading>(heading.Id));
                    break;
                case MathBlock math:
                    EscapeClosingFences(builder, math);
                    break;
                case TableCell cell:
                    if (cell.Text.Contains('\n'))
                    {
                        ReplaceAll(builder, cell, "\n", "<br>");
                    }

                    EscapeCellPipes(builder, builder.Find<TableCell>(cell.Id));
                    break;
                case DefinitionSlot { SlotKind: DefinitionSlotKind.FootnoteContent } slot when slot.Text.Contains('\n'):
                    ReplaceAll(builder, slot, "\n", " ");
                    break;
                case Paragraph paragraph when builder.Before.Composition is null:
                    ApplyZeroWidthSpaceRule(builder, paragraph);
                    break;
            }
        }
    }

    /// <summary>
    /// A paragraph whose first line starts with a tab or four spaces gets a leading zero-width space
    /// so that it does not reopen as indented code; the space goes away when that whitespace does.
    /// </summary>
    public static void ApplyZeroWidthSpaceRule(TransactionBuilder builder, Paragraph paragraph)
    {
        string text = paragraph.Text;
        bool hasMark = text.Length > 0 && text[0] == ZeroWidthSpace;
        bool needsMark = StartsWithCodeIndent(text.AsSpan(hasMark ? 1 : 0));
        if (needsMark && !hasMark)
        {
            builder.InsertText(paragraph.Id, 0, ZeroWidthSpace.ToString());
        }
        else if (hasMark && !needsMark)
        {
            builder.ReplaceText(paragraph.Id, 0, 1, string.Empty);
        }
    }

    /// <summary>
    /// A cell holds '|' only escaped (Q40 default): a pipe after an even number of backslashes
    /// (none included) gets one more, also when an edit removed the backslash of "\|".
    /// </summary>
    private static void EscapeCellPipes(TransactionBuilder builder, TableCell cell)
    {
        string text = cell.Text;
        for (int i = text.Length - 1; i >= 0; i--)
        {
            if (text[i] == '|' && !IsEscaped(text, i))
            {
                builder.InsertText(cell.Id, i, "\\");
            }
        }
    }

    /// <summary>
    /// A heading whose text ends with '#'s after a space would lose them as an ATX closing sequence:
    /// the first one is escaped, "a #" becoming "a \#" (minimal escaping, Q50).
    /// </summary>
    private static void EscapeClosingSequence(TransactionBuilder builder, Heading heading)
    {
        string text = heading.Text.TrimEnd(' ', '\t');
        int start = text.Length;
        while (start > 0 && text[start - 1] == '#')
        {
            start--;
        }

        if (start < text.Length && (start == 0 || text[start - 1] is ' ' or '\t') && !IsEscaped(text, start))
        {
            builder.InsertText(heading.Id, start, "\\");
        }
    }

    /// <summary>
    /// A math block line of only '$'s would close the block: its first '$' is escaped, as Typora does
    /// ("$$" becoming "\$$", Q50).
    /// </summary>
    private static void EscapeClosingFences(TransactionBuilder builder, MathBlock math)
    {
        string text = math.Text;
        var starts = new List<int> { 0 };
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                starts.Add(i + 1);
            }
        }

        // From the last line up, so that an insertion does not move the lines still to check.
        for (int k = starts.Count - 1; k >= 0; k--)
        {
            int end = k + 1 < starts.Count ? starts[k + 1] - 1 : text.Length;
            var line = text.AsSpan(starts[k], end - starts[k]);
            var content = line.TrimStart(' ');
            if (line.Length - content.Length <= 3 && content.StartsWith("$$") && content.TrimStart('$').Trim(" \t").IsEmpty)
            {
                builder.InsertText(math.Id, starts[k] + (line.Length - content.Length), "\\");
            }
        }
    }

    /// <summary>Whether the character at <paramref name="index"/> follows an odd number of backslashes.</summary>
    public static bool IsEscaped(string text, int index)
    {
        int backslashes = 0;
        for (int i = index - 1; i >= 0 && text[i] == '\\'; i--)
        {
            backslashes++;
        }

        return backslashes % 2 == 1;
    }

    public static bool StartsWithCodeIndent(ReadOnlySpan<char> text) =>
        text.StartsWith("\t") || text.StartsWith("    ");

    private static void RemoveEmptyContainers(TransactionBuilder builder)
    {
        var pending = new Queue<BlockId>(builder.Touched);
        while (pending.Count > 0)
        {
            var id = pending.Dequeue();
            if (!DocumentTree.Contains(builder.Document, id) || id == builder.Document.Id)
            {
                continue;
            }

            var location = builder.Locate(id);
            if (location.Block is BlockQuote or ListBlock or ListItem && ((ContainerBlock)location.Block).Children.IsEmpty)
            {
                builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
                pending.Enqueue(location.Parent.Id);
            }
        }
    }

    /// <summary>
    /// An item whose first block is no longer a paragraph loses its task state, which only a paragraph
    /// can carry in Markdown.
    /// </summary>
    private static void NormalizeTaskItems(TransactionBuilder builder)
    {
        foreach (var id in builder.Touched.ToList())
        {
            foreach (var candidate in ItemAndParent(builder, id))
            {
                if (candidate is ListItem { Task: not TaskState.None } item && item.Children[0] is not Paragraph
                    && DocumentTree.Contains(builder.Document, item.Id))
                {
                    builder.ReplaceBlock(builder.Find<ListItem>(item.Id) with { Task = TaskState.None });
                }
            }
        }
    }

    /// <summary>The block (as it is now) and its parent, or nothing when it is gone.</summary>
    private static IEnumerable<Block> ItemAndParent(TransactionBuilder builder, BlockId id)
    {
        if (!DocumentTree.Contains(builder.Document, id) || id == builder.Document.Id)
        {
            return [];
        }

        var location = builder.Locate(id);
        return [location.Block, location.Parent!];
    }

    private static void ReplaceAll(TransactionBuilder builder, LeafBlock leaf, string find, string replacement)
    {
        for (int index = leaf.Text.LastIndexOf(find, StringComparison.Ordinal); index >= 0; index = index == 0 ? -1 : leaf.Text.LastIndexOf(find, index - 1, StringComparison.Ordinal))
        {
            builder.ReplaceText(leaf.Id, index, index + find.Length, replacement);
        }
    }
}
