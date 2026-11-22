using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.Text;

namespace Quill.Editor.Editing;

/// <summary>
/// Keyboard selection of a scope (Q39, Typora's Ctrl+E, Ctrl+L and Ctrl+A). In a table they select
/// the cell, the row and the whole table: plain text selections from the first selected cell to the
/// last, so typing and deleting clear the cells without changing the table (04). Outside a table
/// Ctrl+E selects the formatted span around the caret and Ctrl+L the line. Ctrl+A in a code, math or
/// front matter block selects the block's text first, then the document (Q90).
/// </summary>
internal static class ScopeSelection
{
    /// <summary>Ctrl+E: the cell's text; elsewhere the innermost styled element around the selection, growing outward when pressed again〔默认〕.</summary>
    public static TextSelection? Scope(EditorState state)
    {
        if (Head(state) is not { } head || DocumentTree.TryFind(state.Document, head.Block) is not TextLeafBlock leaf)
        {
            return null;
        }

        if (leaf is TableCell)
        {
            return Range(leaf.Id, 0, leaf.Id, leaf.Text.Length);
        }

        var (start, end) = SameLeafRange(state, leaf) ?? (head.Offset, head.Offset);
        var tree = InlineTrees.Get(state.Document, leaf);

        // The innermost element whose content holds the selection and is larger than it; then its markers too.
        InlineNode? best = null;
        TextRange? target = null;
        foreach (var node in tree.Descendants())
        {
            if (!Activation.CanActivate(node.Kind) || node.Kind is InlineKind.Escape or InlineKind.Entity or InlineKind.HtmlBreak or InlineKind.HardBreak)
            {
                continue;
            }

            TextRange? candidate = node.Content.Start <= start && end <= node.Content.End && node.Content.Length > end - start
                ? node.Content
                : node.Range.Start <= start && end <= node.Range.End && node.Range.Length > end - start ? node.Range : null;
            if (candidate is { } range && (target is null || range.Length < target.Value.Length))
            {
                best = node;
                target = range;
            }
        }

        if (best is not null && target is { } selected)
        {
            return Range(leaf.Id, selected.Start, leaf.Id, selected.End);
        }

        // No styled element: the word at the caret, then the whole block.
        var (wordStart, wordEnd) = TextBoundaries.WordAt(leaf.Text, start);
        return wordEnd - wordStart > end - start && wordStart <= start && end <= wordEnd
            ? Range(leaf.Id, wordStart, leaf.Id, wordEnd)
            : Range(leaf.Id, 0, leaf.Id, leaf.Text.Length);
    }

    /// <summary>Ctrl+L: every cell of the row; elsewhere the visual line at the caret (the source line without a layout)〔默认〕.</summary>
    public static TextSelection? Line(EditorState state, ILayoutService? layout)
    {
        if (Head(state) is not { } head || DocumentTree.TryFind(state.Document, head.Block) is not LeafBlock leaf)
        {
            return null;
        }

        if (leaf is TableCell && DocumentTree.Locate(state.Document, leaf.Id).Parent is TableRow row)
        {
            var first = (LeafBlock)row.Children[0];
            var last = (LeafBlock)row.Children[^1];
            return Range(first.Id, 0, last.Id, last.Text.Length);
        }

        if (layout is not null)
        {
            var line = layout.GetVisualLine(head);
            if (line.Start is TextPosition lineFrom && line.End is TextPosition lineTo && lineFrom.Block == leaf.Id && lineTo.Block == leaf.Id)
            {
                return new TextSelection(lineFrom with { Affinity = Affinity.Forward }, lineTo with { Affinity = Affinity.Backward });
            }
        }

        int lineStart = head.Offset == 0 ? 0 : leaf.Text.LastIndexOf('\n', head.Offset - 1) + 1;
        int lineEnd = leaf.Text.IndexOf('\n', head.Offset);
        return Range(leaf.Id, lineStart, leaf.Id, lineEnd < 0 ? leaf.Text.Length : lineEnd);
    }

    /// <summary>
    /// Ctrl+A: in a table the whole table (Q39, the user's choice over Typora's cell); with the table
    /// already selected, or outside a table, the whole document.
    /// </summary>
    public static TextSelection All(EditorState state)
    {
        var document = new TextSelection(Navigation.First(state.Document, state.Document), Navigation.Last(state.Document, state.Document));
        if (state.Selection.Head is not TextPosition head || DocumentTree.TryFind(state.Document, head.Block) is not { } leaf)
        {
            return document;
        }

        TextSelection whole;
        if (leaf is CodeBlock or MathBlock or FrontMatter or CodeLanguage)
        {
            // Code (Mermaid included), math, front matter and a code block's language: that text first (Q90, as Typora and Feishu).
            whole = Range(leaf.Id, 0, leaf.Id, ((LeafBlock)leaf).Text.Length);
        }
        else if (leaf is TableCell && TableEdits.TableOf(state.Document, head.Block) is Table table)
        {
            var cells = table.Children.Cast<TableRow>().SelectMany(row => row.Children).Cast<LeafBlock>().ToList();
            whole = Range(cells[0].Id, 0, cells[^1].Id, cells[^1].Text.Length);
        }
        else
        {
            return document;
        }

        // Pressed again with the scope already selected (or an empty block): the whole document.
        var (start, end) = PositionOrder.Ordered(state.Document, state.Selection);
        return start.SamePlace(whole.Anchor) && end.SamePlace(whole.Head) ? document : whole;
    }

    private static TextPosition? Head(EditorState state) => state.Selection.Head as TextPosition;

    private static (int Start, int End)? SameLeafRange(EditorState state, LeafBlock leaf)
    {
        var (start, end) = PositionOrder.Ordered(state.Document, state.Selection);
        return start is TextPosition s && end is TextPosition e && s.Block == leaf.Id && e.Block == leaf.Id ? (s.Offset, e.Offset) : null;
    }

    private static TextSelection Range(BlockId startBlock, int start, BlockId endBlock, int end) =>
        new(new TextPosition(startBlock, start), new TextPosition(endBlock, end, Affinity.Backward));
}
