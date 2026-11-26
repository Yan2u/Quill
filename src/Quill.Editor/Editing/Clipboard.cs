using System.Text;
using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Clipboard slices (04-commands-and-behaviors.md, Q9, Q20, Q102, Q119–Q123). Copy cuts the selection out
/// of the document as blocks and writes them with the serializer (Quill's private format), as plain text
/// (Markdown with <see cref="EditorOptions.CopyMarkdownSource"/> on, text as shown otherwise), and as clean
/// semantic CF_HTML when the selection includes tables or spans multiple cells. Paste prioritizes Quill's
/// private format, then CF_HTML tables, then TSV, then standard Markdown fragment parsing.
/// </summary>
internal static class Clipboard
{
    /// <param name="displayed">Plain text as shown whatever the options say (Edit > Copy as plain text).</param>
    public static ClipboardContent Copy(EditorState state, EditorOptions options, bool displayed = false)
    {
        if (state.Selection.IsCollapsed)
        {
            return (options.CopyLineWithoutSelection ? LineClipboard.Copy(state, options, displayed) : null) ?? new ClipboardContent(null, string.Empty);
        }

        var (start, end) = PositionOrder.Ordered(state.Document, state.Selection);
        if (SourceText.IsSource(state.Document))
        {
            // Source mode (11-source-mode.md): the selected text itself, lines joined by "\n".
            string text = SourceText.ToText(new Document { Id = state.Document.Id, Children = [.. Slice(state.Document, start, end).Select(static block => (Block)new SourceLine { Id = block.Id, Text = ((LeafBlock)block).Text })] });
            return new ClipboardContent(text, text);
        }

        var blocks = Slice(state.Document, start, end);
        string markdown = MarkdownSerializer.SerializeFragment(blocks, options);
        string plain = !displayed && options.CopyMarkdownSource
            ? markdown
            : RowText(state.Document, start, end) ?? PlainText(blocks, ReferenceIndex.Of(state.Document), options);
        string? html = ClipboardHtmlWriter.WriteSelection(state.Document, start, end, blocks);
        return new ClipboardContent(markdown, plain, html);
    }

    /// <summary>
    /// A selection across cells of one table row (Ctrl+L, Q39): the cells' source joined by tabs
    /// (Typora joins them with nothing; tabs are the user's choice〔默认〕). Null for other selections.
    /// </summary>
    private static string? RowText(Document document, Position start, Position end)
    {
        if (start is not TextPosition s || end is not TextPosition e || s.Block == e.Block
            || DocumentTree.TryFind(document, s.Block) is not TableCell || DocumentTree.TryFind(document, e.Block) is not TableCell)
        {
            return null;
        }

        var row = DocumentTree.Locate(document, s.Block).Parent;
        if (row is not TableRow || DocumentTree.Locate(document, e.Block).Parent?.Id != row.Id)
        {
            return null;
        }

        var cells = new List<string>();
        bool inside = false;
        foreach (var cell in row.Children.Cast<LeafBlock>())
        {
            inside |= cell.Id == s.Block;
            if (inside)
            {
                int from = cell.Id == s.Block ? s.Offset : 0;
                int to = cell.Id == e.Block ? e.Offset : cell.Text.Length;
                cells.Add(cell.Text[from..to]);
            }

            if (cell.Id == e.Block)
            {
                break;
            }
        }

        return string.Join("\t", cells);
    }

    /// <summary>
    /// Adaptive Ctrl+V paste (Q119, Q121, Q122): raw text leaves get literal text; Quill private
    /// format comes first (preserving body-only table slices inside tables); external CF_HTML tables
    /// come second (mixed text + tables preserved outside tables); structured TSV / pipe tables come
    /// third; plain Markdown fragment parsing is the fallback.
    /// </summary>
    public static Transaction? Paste(EditorState state, ClipboardContent content, BehaviorContext context)
    {
        if (state.Selection.IsCollapsed
            && state.Selection.Head is TextPosition head
            && DocumentTree.TryFind(state.Document, head.Block) is RawTextBlock or CodeLanguage or DefinitionSlot)
        {
            return PastePlainText(state, content.Markdown ?? content.PlainText, context);
        }

        bool inTable = TablePaste.IsInsideSingleTable(state.Document, state.Selection, out _);

        // Priority 1: Quill private format.
        if (content.Markdown is { } privateMarkdown)
        {
            if (inTable)
            {
                if (content.Html is { Length: > 0 }
                    && HtmlTableExtractor.Extract(content.Html) is { Segments: [HtmlTableSegment { Grid: { IsBodyOnly: true, IsSingleCell: false } bodyGrid }] })
                {
                    return TablePaste.PasteIntoTable(state, bodyGrid, context);
                }

                if (TryParseSingleGfmTable(privateMarkdown) is { IsSingleCell: false } privateGrid)
                {
                    return TablePaste.PasteIntoTable(state, privateGrid, context);
                }
            }

            return PastePlainText(state, privateMarkdown, context);
        }

        // Priority 2: CF_HTML tables (including mixed text + table fragments).
        if (content.Html is { Length: > 0 } rawHtml && HtmlTableExtractor.Extract(rawHtml) is { } extracted)
        {
            if (inTable)
            {
                if (extracted.Segments is [HtmlTableSegment { Grid.IsSingleCell: false } tableSeg])
                {
                    return TablePaste.PasteIntoTable(state, tableSeg.Grid, context);
                }

                if (extracted.Segments is [HtmlTextSegment singleCellText])
                {
                    return PastePlainText(state, singleCellText.Markdown, context);
                }

                if (extracted.HasTable)
                {
                    return PastePlainText(state, content.PlainText, context);
                }
            }
            else if (extracted.HasTable)
            {
                var builder = context.Begin(state);
                EditOperations.DeleteSelection(builder);
                var caret = TextBehavior.EnsureTextCaret(builder);
                var leaf = builder.Find<LeafBlock>(caret.Block);
                var blocks = MaterializeHtmlSegments(extracted.Segments, builder.Ids);
                if (blocks.Count > 0 && leaf is Paragraph or Heading)
                {
                    return PasteBlocks(builder, leaf, caret.Offset, blocks, context);
                }
            }
            else if (extracted.Segments is [HtmlTextSegment singleText])
            {
                return PastePlainText(state, singleText.Markdown, context);
            }
        }

        // Priority 3: TSV (or GFM pipe table when inside a table).
        if (inTable)
        {
            if (TsvTableParser.TryParse(content.PlainText, insideTable: true) is { IsSingleCell: false } tsvInside)
            {
                return TablePaste.PasteIntoTable(state, tsvInside, context);
            }

            if (TryParseSingleGfmTable(content.PlainText) is { IsSingleCell: false } pipeInside)
            {
                return TablePaste.PasteIntoTable(state, pipeInside, context);
            }
        }
        else if (TsvTableParser.TryParse(content.PlainText, insideTable: false) is { IsEmpty: false, IsSingleCell: false } tsvOutside)
        {
            var builder = context.Begin(state);
            EditOperations.DeleteSelection(builder);
            var caret = TextBehavior.EnsureTextCaret(builder);
            var leaf = builder.Find<LeafBlock>(caret.Block);
            if (leaf is Paragraph or Heading)
            {
                return PasteBlocks(builder, leaf, caret.Offset, [tsvOutside.ToTable(builder.Ids)], context);
            }
        }

        // Priority 4: Standard Markdown fragment paste.
        return PastePlainText(state, content.PlainText, context);
    }

    /// <summary>
    /// Replaces the selection with pasted Markdown (used by Ctrl+Shift+V without HTML or TSV table
    /// conversion, and as the plain-text Markdown fallback). Raw text leaves take the text as it is;
    /// cells, definition slots and headings take it as one line.
    /// </summary>
    public static Transaction? PastePlainText(EditorState state, string text, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var caret = TextBehavior.EnsureTextCaret(builder);
        var leaf = builder.Find<LeafBlock>(caret.Block);
        var blocks = leaf is Paragraph or Heading ? MarkdownImporter.ImportFragment(text, builder.Ids) : [];
        if (blocks.Count == 0 || (blocks.Count == 1 && blocks[0] is Paragraph) || leaf is not (Paragraph or Heading))
        {
            if (builder.Locate(leaf.Id).Parent is not Document && blocks.Count == 1)
            {
                blocks = [FragmentSyntax.WithoutTabs(blocks[0])];
            }

            string inline = TextBehavior.AdaptText(leaf, blocks.Count == 1 ? ((Paragraph)blocks[0]).Text : text);
            builder.InsertText(caret.Block, caret.Offset, inline);
            builder.SetCaret(caret.Block, caret.Offset + inline.Length);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        return PasteBlocks(builder, leaf, caret.Offset, blocks, context);
    }

    private static TableGrid? TryParseSingleGfmTable(string markdown)
    {
        if (!markdown.Contains('|'))
        {
            return null;
        }

        var blocks = MarkdownImporter.ImportFragment(markdown, new SequentialBlockIdSource());
        return blocks is [Table table] ? TableGrid.FromTable(table) : null;
    }

    private static List<Block> MaterializeHtmlSegments(IReadOnlyList<HtmlPasteSegment> segments, IBlockIdSource ids)
    {
        var blocks = new List<Block>();
        foreach (var segment in segments)
        {
            switch (segment)
            {
                case HtmlTableSegment tableSeg when !tableSeg.Grid.IsEmpty:
                    blocks.Add(tableSeg.Grid.ToTable(ids));
                    break;
                case HtmlTextSegment textSeg when textSeg.Markdown.Length > 0:
                    blocks.AddRange(MarkdownImporter.ImportFragment(textSeg.Markdown, ids));
                    break;
            }
        }

        return blocks;
    }

    private static Transaction? PasteBlocks(
        TransactionBuilder builder,
        LeafBlock leaf,
        int caretOffset,
        IReadOnlyList<Block> blocks,
        BehaviorContext context)
    {
        if (builder.Locate(leaf.Id).Parent is not Document)
        {
            blocks = [.. blocks.Select(FragmentSyntax.WithoutTabs)];
        }

        if (blocks.Count == 1 && blocks[0] is Paragraph singleParagraph)
        {
            string inline = TextBehavior.AdaptText(leaf, singleParagraph.Text);
            builder.InsertText(leaf.Id, caretOffset, inline);
            builder.SetCaret(leaf.Id, caretOffset + inline.Length);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (blocks[0] is ListBlock && PasteIntoList(builder, leaf, caretOffset, blocks))
        {
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        var location = builder.Locate(leaf.Id);
        string tail = leaf.Text[caretOffset..];
        builder.ReplaceText(leaf.Id, caretOffset, leaf.Text.Length, string.Empty);
        var inserted = blocks.ToList();
        inserted[0] = inserted[0] with { SeparatorBefore = null };
        int insertAt = location.Index + 1;
        if (inserted[0] is Paragraph first)
        {
            builder.InsertText(leaf.Id, caretOffset, TextBehavior.AdaptText(leaf, first.Text));
            inserted.RemoveAt(0);
        }
        else if (caretOffset == 0 && leaf is Paragraph && !(location.Parent is ListItem && location.Index == 0))
        {
            // Nothing before the caret: the pasted blocks take the paragraph's place (a list item
            // keeps its first paragraph, its row).
            builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
            insertAt = location.Index;
        }

        Position after;
        if (JoinTail(inserted[^1], tail, out var joined) is { } withTail)
        {
            after = joined;
            inserted[^1] = withTail;
        }
        else
        {
            // After a pasted code block the caret stands at the end of its code, not in its language (Q91).
            var end = inserted[^1] is CodeFence code ? new TextPosition(code.Code.Id, code.Code.Text.Length) : CaretStops.Edge(inserted[^1], forward: false);
            Paragraph? tailParagraph = tail.Length > 0 ? EditOperations.NewParagraph(builder, tail) : null;
            if (tailParagraph is not null)
            {
                inserted.Add(tailParagraph);
            }

            after = end ?? (tailParagraph is not null
                ? new TextPosition(tailParagraph.Id, 0)
                : new BoundaryPosition(location.Parent!.Id, insertAt + inserted.Count));
        }

        builder.InsertBlocks(location.Parent!.Id, insertAt, [.. inserted]);
        builder.SetCaret(after);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Pastes blocks that start with a list into a paragraph that is a list row: the pasted rows join
    /// the list at the paragraph's level (02: pasting items adds items), the first pasted item's text
    /// joining the text before the caret; blocks after the pasted list leave the list as level-0 rows;
    /// the text after the caret joins the last pasted paragraph. False when the paragraph is in no
    /// list or the pasted list cannot be shown as rows.
    /// </summary>
    private static bool PasteIntoList(TransactionBuilder builder, LeafBlock leaf, int offset, IReadOnlyList<Block> blocks)
    {
        if (leaf is not Paragraph || Lists.ListRowView.ForBlock(builder.Document, leaf.Id) is not { } probe || probe.IndexOf(leaf.Id) < 0)
        {
            return false;
        }

        int level = probe.Rows[probe.IndexOf(leaf.Id)].Level;
        var pasted = probe.RowsOf((ListBlock)blocks[0], level);

        string tail = leaf.Text[offset..];
        builder.ReplaceText(leaf.Id, offset, leaf.Text.Length, string.Empty);
        int junction = offset;
        if (pasted[0].Block is Paragraph first)
        {
            builder.InsertText(leaf.Id, offset, first.Text);
            junction += first.Text.Length;
            pasted.RemoveAt(0);
        }

        foreach (var block in blocks.Skip(1))
        {
            pasted.Add(new Lists.ListRow { Block = block, Kind = Lists.ListRowKind.Content, Level = 0, OriginalList = (ListBlock)blocks[0] });
        }

        Position after = new TextPosition(leaf.Id, junction);
        if (pasted.Count > 0 && pasted[^1].Block is { } lastBlock && JoinTail(lastBlock, tail, out var joined) is { } withTail)
        {
            after = joined;
            pasted[^1] = pasted[^1] with { Block = withTail };
        }
        else if (pasted.Count == 0)
        {
            builder.InsertText(leaf.Id, junction, tail);
        }
        else if (tail.Length > 0)
        {
            var tailParagraph = EditOperations.NewParagraph(builder, tail);
            pasted.Add(new Lists.ListRow { Block = tailParagraph, Kind = Lists.ListRowKind.Content, Level = pasted[^1].Level, OriginalList = (ListBlock)blocks[0] });
            after = (pasted[^2].Block is { } beforeTail ? CaretStops.Edge(beforeTail, forward: false) : null) ?? new TextPosition(tailParagraph.Id, 0);
        }
        else
        {
            after = (pasted[^1].Block is { } last ? CaretStops.Edge(last, forward: false) : null) ?? after;
        }

        // Rebuild the view so that its rows hold the edited paragraph; it learns the pasted lists too.
        var view = Lists.ListRowView.ForBlock(builder.Document, leaf.Id)!;
        view.RowsOf((ListBlock)blocks[0], level);
        view.Rows.InsertRange(view.IndexOf(leaf.Id) + 1, pasted.Select(row => row.Block is null ? row : row with { Block = row.Block with { SeparatorBefore = null } }));
        view.WriteBack(builder);
        builder.SetCaret(after);
        return true;
    }

    /// <summary>
    /// The pasted content is open at its end: the text after the caret joins its last paragraph, also
    /// inside a quote or list. Returns the block with the text appended and where the pasted text
    /// ends, or null when the block does not end with a paragraph.
    /// </summary>
    private static Block? JoinTail(Block block, string tail, out TextPosition end)
    {
        switch (block)
        {
            case Paragraph paragraph:
                end = new TextPosition(paragraph.Id, paragraph.Text.Length);
                return paragraph with { Text = paragraph.Text + tail };
            case BlockQuote or ListBlock or ListItem when ((ContainerBlock)block).Children is { Count: > 0 } children
                && JoinTail(children[^1], tail, out end) is { } last:
                return ((ContainerBlock)block) with { Children = children.SetItem(children.Count - 1, last) };
            default:
                end = null!;
                return null;
        }
    }

    /// <summary>
    /// The blocks between two positions: the first and last leaves are cut at the positions, blocks in
    /// between are taken whole. Tables and definitions keep their structure, with the text outside the
    /// range left out; a table cut below its header keeps the header row.
    /// </summary>
    public static IReadOnlyList<Block> Slice(Document document, Position start, Position end)
    {
        if (start is TextPosition s && end is TextPosition e && s.Block == e.Block)
        {
            string text = DocumentTree.Find<LeafBlock>(document, s.Block).Text[s.Offset..e.Offset];
            return [new Paragraph { Id = s.Block, Text = text }];
        }

        var startPath = DocumentPath.Of(document, start);
        var endPath = DocumentPath.Of(document, end);
        int common = DocumentPath.CommonLevel(startPath, endPath);
        var container = startPath[common].Container;
        var startStep = startPath[common];
        var endStep = endPath[common];
        int last = endStep.IsChild ? endStep.Index : endStep.Index - 1;
        var blocks = new List<Block>();
        for (int index = startStep.Index; index <= last && index < container.Children.Count; index++)
        {
            var block = container.Children[index];
            if (index == startStep.Index && startStep.IsChild)
            {
                block = CutBefore(block, startPath, common + 1, start);
            }

            if (index == endStep.Index && endStep.IsChild)
            {
                block = CutAfter(block, endPath, common + 1, end);
            }

            blocks.Add(index == startStep.Index ? block with { SeparatorBefore = null } : block);
        }

        switch (container)
        {
            case Table table:
                // Rows alone are no blocks: they stay in their table, under its header row.
                if (startStep.Index > 0)
                {
                    blocks.Insert(0, table.Children[0]);
                }

                return [table with { Children = [.. blocks], SeparatorBefore = null }];
            case ListBlock list:
                return [list with { Children = [.. blocks], SeparatorBefore = null }];
            case TableRow or FootnoteDefinition or LinkReferenceDefinition or CodeFence:
                return [Rebuild(container, blocks, startStep.Index, document)];
            default:
                return blocks;
        }
    }

    /// <summary>
    /// The selected text as displayed: leaves joined by line breaks, without hidden inline syntax or
    /// leading zero-width spaces (Q9, Q20). Reference links resolve against the copied-from document.
    /// Tables (several rows or a whole table) are written as their Markdown source, the header row
    /// first (Q39, as Typora does).
    /// </summary>
    public static string PlainText(IEnumerable<Block> blocks, ReferenceIndex references, EditorOptions? options = null)
    {
        var builder = new StringBuilder();
        foreach (var block in blocks)
        {
            var parts = block is Table
                ? [MarkdownSerializer.SerializeFragment([block], options).TrimEnd('\n')]
                : Navigation.Leaves(block).Where(static leaf => leaf is not CodeLanguage).Select(leaf => Projection.PlainText.Of(leaf, references));
            foreach (string part in parts)
            {
                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(part);
            }
        }

        return builder.ToString();
    }

    /// <summary>The part of a block from the position on.</summary>
    private static Block CutBefore(Block block, List<DocumentPath.Step> path, int level, Position position)
    {
        if (level >= path.Count)
        {
            var leaf = (LeafBlock)block;
            int offset = ((TextPosition)position).Offset;
            return leaf with { Text = leaf.Text[offset..], LinePrefixes = [] };
        }

        var step = path[level];
        var container = (ContainerBlock)block;
        var children = new List<Block>();
        if (IsStructural(container))
        {
            for (int i = 0; i < container.Children.Count; i++)
            {
                children.Add(i < step.Index ? ClearText(container.Children[i])
                    : i == step.Index && step.IsChild ? CutBefore(container.Children[i], path, level + 1, position)
                    : container.Children[i]);
            }
        }
        else
        {
            int from = step.Index;
            if (step.IsChild && from < container.Children.Count)
            {
                children.Add(CutBefore(container.Children[from], path, level + 1, position) with { SeparatorBefore = null });
                from++;
            }

            children.AddRange(container.Children.Skip(from));
        }

        return container with { Children = [.. children] };
    }

    /// <summary>The part of a block before the position.</summary>
    private static Block CutAfter(Block block, List<DocumentPath.Step> path, int level, Position position)
    {
        if (level >= path.Count)
        {
            var leaf = (LeafBlock)block;
            int offset = ((TextPosition)position).Offset;
            return leaf with { Text = leaf.Text[..Math.Min(offset, leaf.Text.Length)] };
        }

        var step = path[level];
        var container = (ContainerBlock)block;
        var children = new List<Block>();
        if (IsStructural(container))
        {
            for (int i = 0; i < container.Children.Count; i++)
            {
                children.Add(i > step.Index || (i == step.Index && !step.IsChild) ? ClearText(container.Children[i])
                    : i == step.Index ? CutAfter(container.Children[i], path, level + 1, position)
                    : container.Children[i]);
            }
        }
        else
        {
            children.AddRange(container.Children.Take(step.Index));
            if (step.IsChild && step.Index < container.Children.Count)
            {
                children.Add(CutAfter(container.Children[step.Index], path, level + 1, position));
            }
        }

        return container with { Children = [.. children] };
    }

    /// <summary>Containers whose children cannot be dropped: table rows, definition slots and the slots of a code block.</summary>
    private static bool IsStructural(ContainerBlock container) => container is TableRow or FootnoteDefinition or LinkReferenceDefinition or CodeFence;

    private static Block ClearText(Block block) => block switch
    {
        LeafBlock leaf => leaf with { Text = string.Empty, LinePrefixes = [] },
        ContainerBlock container => container with { Children = [.. container.Children.Select(ClearText)] },
        _ => block,
    };

    /// <summary>A slice that lies inside a table row or a definition: the container with the slice's children and the rest cleared.</summary>
    private static Block Rebuild(ContainerBlock container, List<Block> slice, int firstIndex, Document document)
    {
        var children = new List<Block>(container.Children.Count);
        for (int i = 0; i < container.Children.Count; i++)
        {
            int k = i - firstIndex;
            children.Add(k >= 0 && k < slice.Count ? slice[k] : ClearText(container.Children[i]));
        }

        Block result = container with { Children = [.. children], SeparatorBefore = null };
        if (container is TableRow)
        {
            // A row alone is no block: it goes into its table, under the header row.
            var location = DocumentTree.Locate(document, container.Id);
            var table = (Table)location.Parent!;
            result = table with
            {
                Children = location.Index == 0 ? [result] : [table.Children[0], result],
                SeparatorBefore = null,
            };
        }

        return result;
    }
}
