using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Commits a paragraph (02-document-model.md, 06-markdown-io.md): its source is parsed again as
/// Markdown, and when the result is no longer a single paragraph it replaces the paragraph. The first
/// block keeps the paragraph's ID; later blocks are new and get generated separators (a blank line,
/// Typora's auto-formatting, Q33). The caret follows the text it was in.
/// </summary>
internal static class ParagraphCommit
{
    public static Transaction? TryCommit(EditorState state, BlockId paragraphId, IBlockIdSource ids, DateTimeOffset now)
    {
        var builder = new TransactionBuilder(state, ids);
        return Commit(builder, paragraphId) ? builder.Build(TransactionKind.Commit, now) : null;
    }

    /// <summary>Commits a paragraph within a transaction being built (find and replace commits the paragraphs it changed); false when nothing changes.</summary>
    public static bool Commit(TransactionBuilder builder, BlockId paragraphId)
    {
        if (!DocumentTree.Contains(builder.Document, paragraphId)
            || builder.Find<Block>(paragraphId) is not Paragraph paragraph
            || paragraph.IsEmpty)
        {
            return false;
        }

        var blocks = MarkdownImporter.ImportFragment(paragraph.Text, builder.Ids);
        if (blocks.Count == 1 && blocks[0] is Paragraph single && single.Text == paragraph.Text)
        {
            return false;
        }

        var location = DocumentTree.Locate(builder.Document, paragraphId);
        if (location.Parent is not Document)
        {
            blocks = [.. blocks.Select(FragmentSyntax.WithoutTabs)];
        }

        var caret = builder.Selection.Head as TextPosition;
        var mapped = caret is not null && caret.Block == paragraphId ? MapOffset(paragraph.Text, caret.Offset, blocks) : null;

        // The first block takes over the paragraph's ID, also where the blocks refer to it (recorded
        // line prefixes of its contents, the list after it written apart from it).
        var firstId = blocks[0].Id;
        var replaced = new List<Block>(blocks.Count);
        for (int i = 0; i < blocks.Count; i++)
        {
            var block = BlockReferences.Rename(blocks[i], firstId, paragraphId) with { SeparatorBefore = i == 0 ? paragraph.SeparatorBefore : null };
            if (i == 0 && mapped is { } first && first.Block == firstId)
            {
                mapped = first with { Block = paragraphId };
            }

            replaced.Add(block);
        }

        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
        builder.InsertBlocks(location.Parent.Id, location.Index, [.. replaced]);
        if (mapped is not null)
        {
            builder.SetCaret(mapped);
        }
        else if (caret is not null && caret.Block == paragraphId)
        {
            builder.SetCaret(Navigation.Last(builder.Document, replaced[^1]));
        }

        return true;
    }

    /// <summary>
    /// Finds the leaf and offset that hold the source offset after re-parsing: every line of a new
    /// leaf is the end of a line of the original text (block syntax and prefixes are cut from the start).
    /// </summary>
    private static TextPosition? MapOffset(string text, int offset, IReadOnlyList<Block> blocks)
    {
        var originalLines = text.Split('\n');
        int caretLine = text.AsSpan(0, offset).Count('\n');
        int caretColumn = offset - (text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1);
        if (offset > 0 && text[offset - 1] == '\n')
        {
            caretColumn = 0;
        }

        int searchFrom = 0;
        foreach (var leaf in blocks.SelectMany(Navigation.Leaves))
        {
            var leafLines = leaf.Text.Split('\n');
            int leafOffset = 0;
            for (int i = 0; i < leafLines.Length; i++)
            {
                int line = FindLine(originalLines, leafLines[i], searchFrom);
                if (line < 0)
                {
                    break;
                }

                searchFrom = line + 1;
                if (line == caretLine)
                {
                    int cut = originalLines[line].Length - leafLines[i].Length;
                    return new TextPosition(leaf.Id, leafOffset + Math.Clamp(caretColumn - cut, 0, leafLines[i].Length));
                }

                leafOffset += leafLines[i].Length + 1;
            }
        }

        return null;
    }

    private static int FindLine(string[] lines, string suffix, int from)
    {
        for (int line = from; line < lines.Length; line++)
        {
            if (lines[line].EndsWith(suffix, StringComparison.Ordinal))
            {
                return line;
            }
        }

        return -1;
    }
}
