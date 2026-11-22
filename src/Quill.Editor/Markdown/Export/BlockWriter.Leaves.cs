using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Export;

internal sealed partial class BlockWriter
{
    private void WriteParagraph(Paragraph paragraph, ListItem? ownerItem)
    {
        // Leading spaces do not show; where they would join a list item's marker spacing or the list
        // before the paragraph they are left out (Q50).
        bool task = ownerItem is { Task: not TaskState.None } && ReferenceEquals(ownerItem.Children[0], paragraph);
        string text = task || paragraph.IsEmpty ? paragraph.Text : Unabsorbed(paragraph.Text);
        _writer.BeginLine(paragraph.PrefixAt(0));
        if (task)
        {
            _writer.Write(TaskMarker(ownerItem!));
        }

        _writer.WriteLines(text, paragraph.LinePrefixes);
    }

    /// <summary>The recorded task marker when it still matches the task state; otherwise a generated one.</summary>
    private static string TaskMarker(ListItem item)
    {
        string? written = item.Syntax?.TaskMarker;
        bool writtenChecked = written is not null && written[1] != ' ';
        if (written is not null && writtenChecked == (item.Task == TaskState.Checked))
        {
            return written;
        }

        string box = item.Task == TaskState.Checked ? "[x]" : "[ ]";
        return box + (written is not null ? written[3..] : " ");
    }

    private void WriteHeading(Heading heading)
    {
        var syntax = heading.Syntax;
        // An empty heading cannot be a Setext heading (an underline after a blank line is a rule).
        if (syntax is { Style: HeadingStyle.Setext } && heading.Level <= 2 && !string.IsNullOrWhiteSpace(heading.Text))
        {
            // Leading spaces of the text do not show either; they are left out where they would join
            // a list item's marker spacing or the list before the heading (Q50).
            string text = Unabsorbed(syntax.Opening + heading.Text);

            // Edited text that would not read back as a Setext heading (such as "$$") is written as ATX.
            if (heading.Text == syntax.SetextText || ReadsAsSetextHeading(text, heading.Level))
            {
                _writer.BeginLine(heading.PrefixAt(0));
                _writer.WriteLines(text, heading.LinePrefixes);
                _writer.Write(syntax.Closing);
                var underline = syntax.Level == heading.Level && syntax.Underline is { } written
                    ? written
                    : new SyntaxLine(heading.Level == 1 ? "===" : "---", syntax.Underline?.Prefix ?? LinePrefix.None);
                _writer.BeginLine(underline.Prefix);
                _writer.Write(underline.Text);
                return;
            }
        }

        string opening;
        string closing;
        if (syntax is { Style: HeadingStyle.Atx } && syntax.Level == heading.Level)
        {
            // An empty heading may have been written without a space after its '#'s, or before its
            // closing sequence ("# #"): text needs them.
            opening = syntax.Opening;
            closing = syntax.Closing;
            if (heading.Text.Length > 0 && opening.Length > 0 && opening[^1] == '#')
            {
                opening += " ";
            }

            if (heading.Text.Length > 0 && closing.Length > 0 && closing[0] == '#')
            {
                closing = " " + closing;
            }
        }
        else
        {
            // Keep the indentation of an ATX opening; a Setext heading that left levels 1-2 becomes ATX.
            string indent = syntax is { Style: HeadingStyle.Atx } ? syntax.Opening[..^syntax.Opening.TrimStart(' ').Length] : string.Empty;
            opening = indent + new string('#', heading.Level) + " ";
            closing = string.Empty;
        }

        opening = Unabsorbed(opening);
        _writer.BeginLine(heading.PrefixAt(0));
        _writer.Write(opening);
        _writer.WriteLines(heading.Text, heading.LinePrefixes);
        _writer.Write(closing);
    }

    /// <summary>Whether the text over an underline reads back as a Setext heading with that text.</summary>
    private static bool ReadsAsSetextHeading(string text, int level)
    {
        string underline = level == 1 ? "===" : "---";
        return MarkdownImporter.ImportFragment(text + "\n" + underline, new SequentialBlockIdSource()) is [Heading heading]
            && heading.Level == level && heading.Text == text.Trim(' ', '\t');
    }

    private void WriteCodeBlock(CodeFence fence, bool isLast)
    {
        var code = fence.Code;
        // Indented code right after a list, indented as far as the list's last item, would continue
        // that item: it is fenced instead (Q50). So is indented code whose first or last line became
        // blank by editing (blank lines around indented code are not part of it), and indented code
        // right after other indented code, which it would join. Indented code that got a language is
        // fenced: only a fence can carry it (Q91).
        string[] lines = code.Text.Split('\n');
        int indentColumns = 4 + Columns(lines[0][..^lines[0].TrimStart(' ', '\t').Length]);
        bool blankEdge = lines[0].Trim(' ', '\t').Length == 0 || lines[^1].Trim(' ', '\t').Length == 0;
        _wroteIndentedCode = fence.Syntax is { Style: CodeBlockStyle.Indented } && fence.Info.Length == 0 && indentColumns < _absorbedFrom && !blankEdge && !_codeAfterIndentedCode;
        if (_wroteIndentedCode)
        {
            _writer.PushScope(new OwnIndentScope(code.Id, "    ", reuseStored: true, exact: true));
            WriteLeafLines(code);
            _writer.PopScope();
            return;
        }

        WriteFenced(code, fence.Syntax?.Fence, fence.Info, defaultChar: '`', defaultLength: 3, canLengthen: true, allowUnclosed: isLast);
    }

    /// <summary>
    /// Writes a fenced block. The recorded fences are reused while the info string is unchanged and no
    /// content line, as written, would close them; otherwise new fences are generated (longer than any
    /// fence-like line in the content when <paramref name="canLengthen"/>).
    /// </summary>
    private void WriteFenced(LeafBlock block, FenceSyntax? fence, string info, char defaultChar, int defaultLength, bool canLengthen, bool allowUnclosed)
    {
        bool reuse = fence is not null && fence.Info == info;
        char fenceChar = fence?.FenceChar ?? defaultChar;
        int length = fence?.FenceLength ?? defaultLength;
        if (canLengthen)
        {
            if (reuse && LongestClosingRun(block, fenceChar, fence) >= length)
            {
                reuse = false;
            }

            if (!reuse)
            {
                if (fenceChar == '`' && info.Contains('`'))
                {
                    fenceChar = '~';
                }

                length = Math.Max(length, LongestClosingRun(block, fenceChar, fence: null) + 1);
            }
        }

        string fenceText = new(fenceChar, length);
        string opening = reuse ? Unabsorbed(fence!.Opening.Text) : fenceText + info;
        bool keepIndent = reuse && opening.Length == fence!.Opening.Text.Length;
        _writer.BeginLine(fence?.Opening.Prefix ?? LinePrefix.None);
        _writer.Write(opening);

        bool emptyFence = reuse && fence!.HasNoContentLines && block.Text.Length == 0;
        if (!emptyFence)
        {
            string ownIndent = keepIndent ? new string(' ', fence!.IndentColumns) : string.Empty;
            _writer.PushScope(new OwnIndentScope(block.Id, ownIndent, reuseStored: keepIndent));
            WriteLeafLines(block);
            _writer.PopScope();
        }

        bool keepUnclosed = reuse && fence!.Closing is null && allowUnclosed && fence.UnclosedText == block.Text;
        if (keepUnclosed)
        {
            // Left open as in the source, unless a blank line that would join the code follows.
            _writer.DeferClosingFence(fenceText);
        }
        else
        {
            var closing = reuse ? fence!.Closing : null;
            _writer.BeginLine(closing?.Prefix ?? fence?.Closing?.Prefix ?? LinePrefix.None);
            _writer.Write(closing is null ? fenceText : keepIndent ? closing.Value.Text : closing.Value.Text.TrimStart(' ', '\t'));
        }
    }

    /// <summary>
    /// The longest run of the fence character on a content line that could close a fence once
    /// written: at most three columns of indentation (the block's own indentation included, taken from
    /// <paramref name="fence"/> when its recorded indentation is reused; tabs measured from the column
    /// where the content starts) and nothing after the run.
    /// </summary>
    private int LongestClosingRun(LeafBlock block, char fenceChar, FenceSyntax? fence)
    {
        int containerColumn = _writer.ContentColumn();
        int longest = 0;
        int lineIndex = 0;
        foreach (var line in block.Text.Split('\n'))
        {
            int ownIndent = 0;
            if (fence is not null)
            {
                ownIndent = block.PrefixAt(lineIndex).TryGetSegment(block.Id, out var segment) ? Columns(segment) : fence.IndentColumns;
            }

            lineIndex++;
            int column = containerColumn + ownIndent;
            int start = 0;
            for (; start < line.Length && line[start] is ' ' or '\t'; start++)
            {
                column = line[start] == '\t' ? column + 4 - (column % 4) : column + 1;
            }

            if (column - containerColumn > 3)
            {
                continue;
            }

            var rest = line.AsSpan(start);
            int run = rest.Length - rest.TrimStart(fenceChar).Length;
            if (run > 0 && rest[run..].Trim(" \t").IsEmpty)
            {
                longest = Math.Max(longest, run);
            }
        }

        return longest;
    }

    /// <summary>Writes the lines of a raw text block (code, math, HTML, front matter).</summary>
    private void WriteLeafLines(LeafBlock leaf)
    {
        _writer.BeginLine(leaf.PrefixAt(0));
        _writer.WriteLines(leaf.Text, leaf.LinePrefixes);
    }

    private void WriteAtomic(AtomicBlock block, string generated)
    {
        string text = Unabsorbed(block.Syntax?.Text ?? generated);
        _writer.BeginLine(block.Syntax?.Prefix ?? LinePrefix.None);
        _writer.Write(text);
    }

    /// <summary>
    /// A generated rule is "---", except as the document's first line (where it would open YAML front
    /// matter) and on the marker line of a '-' item (where "- ---" is itself a rule line).
    /// </summary>
    private void WriteThematicBreak(ThematicBreak rule)
    {
        // On an item's marker line a rule of the bullet's own character would make the whole line a
        // rule ("- ---", "* ***"): the recorded rule gives way to one of another character.
        char? bullet = _writer.PendingItemBullet();
        string generated = _writer.IsAtDocumentStart || bullet == '-' ? "***" : "---";
        if (bullet is '-' or '*' && rule.Syntax is { } syntax && syntax.Text.Trim().TrimStart(bullet.Value, ' ', '\t').Length == 0)
        {
            rule = rule with { Syntax = null };
            generated = bullet == '-' ? "***" : "---";
        }

        WriteAtomic(rule, generated);
    }

    private void WriteTable(Table table)
    {
        WriteTableRow((TableRow)table.Children[0]);
        var syntax = table.Syntax;
        _writer.BeginLine(syntax?.DelimiterRow.Prefix ?? LinePrefix.None);
        _writer.Write(syntax is not null && syntax.Alignments.SequenceEqual(table.Alignments)
            ? syntax.DelimiterRow.Text
            : GenerateDelimiterRow(table.Alignments));
        for (int index = 1; index < table.Children.Count; index++)
        {
            WriteTableRow((TableRow)table.Children[index]);
        }
    }

    private void WriteTableRow(TableRow row)
    {
        var syntax = row.Syntax;
        _writer.MarkBlockStart(row.Id);
        _writer.BeginLine(syntax?.Prefix ?? LinePrefix.None);
        if (syntax is not null && RowMatchesSyntax(row, syntax))
        {
            _writer.Write(syntax.Gaps[0]);
            for (int index = 0; index < syntax.Cells.Length; index++)
            {
                var cell = (TableCell)row.Children[index];
                _writer.WriteLines(cell.Text, cell.LinePrefixes);

                // A cell ending in a backslash would escape the pipe right after it.
                string gap = syntax.Gaps[index + 1];
                _writer.Write(gap.StartsWith('|') && EndsWithEscape(cell.Text) ? " " + gap : gap);
            }

            return;
        }

        _writer.Write("|");
        foreach (var cell in row.Children.Cast<TableCell>())
        {
            _writer.Write(" ");
            _writer.WriteLines(cell.Text, cell.LinePrefixes);
            _writer.Write(" |");
        }
    }

    /// <summary>Whether the text ends in a backslash that is not itself escaped.</summary>
    private static bool EndsWithEscape(string text) => text.Length > 0 && text[^1] == '\\' && !Transactions.Normalizer.IsEscaped(text, text.Length - 1);

    /// <summary>The row still holds the written cells in order, followed only by empty padding cells.</summary>
    private static bool RowMatchesSyntax(TableRow row, TableRowSyntax syntax)
    {
        if (row.Children.Count < syntax.Cells.Length || syntax.Gaps.Length != syntax.Cells.Length + 1)
        {
            return false;
        }

        for (int index = 0; index < row.Children.Count; index++)
        {
            var cell = row.Children[index];
            if (index < syntax.Cells.Length ? cell.Id != syntax.Cells[index] : ((TableCell)cell).Text.Length > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string GenerateDelimiterRow(IReadOnlyList<TableAlignment> alignments)
    {
        var builder = new System.Text.StringBuilder("|");
        foreach (var alignment in alignments)
        {
            builder.Append(alignment switch
            {
                TableAlignment.Left => " :--- |",
                TableAlignment.Center => " :---: |",
                TableAlignment.Right => " ---: |",
                _ => " --- |",
            });
        }

        return builder.ToString();
    }

    private void WriteFootnote(FootnoteDefinition footnote)
    {
        var syntax = footnote.Syntax;
        _writer.PushScope(new FootnoteScope(footnote.Id));
        _writer.BeginLine(syntax?.Prefix ?? LinePrefix.None);
        _writer.Write(syntax?.Opening ?? "[^");
        _writer.WriteLines(footnote.Name.Text, footnote.Name.LinePrefixes);
        _writer.Write(syntax?.Separator ?? "]: ");
        _writer.WriteLines(footnote.Content.Text, footnote.Content.LinePrefixes);
        _footnoteDepth++;
        WriteBody(footnote.Children.GetRange(2, footnote.Children.Count - 2), BodyContext.Blocks, ownerItem: null, previousIsContent: true);
        _footnoteDepth--;
        _writer.PopScope();
    }

    private void WriteLinkDefinition(LinkReferenceDefinition definition)
    {
        var syntax = definition.Syntax;
        string destination = definition.Destination.Text;
        bool hasTitle = definition.Title.Text.Length > 0;

        // A definition without a label is not Markdown: an empty one gets a placeholder.
        var label = LinkLabelPlaceholders.IsBlank(definition.Label.Text)
            ? (Text: _placeholders.Next(), Prefixes: ImmutableArray<LinePrefix>.Empty)
            : (definition.Label.Text, Prefixes: definition.Label.LinePrefixes);
        bool reuse = syntax is not null && (syntax.IsPointy || !NeedsAngleBrackets(destination));
        _writer.BeginLine(syntax?.Opening.PrefixAt(0) ?? LinePrefix.None);
        if (!reuse)
        {
            bool pointy = NeedsAngleBrackets(destination);
            _writer.Write("[");
            _writer.WriteLines(label.Text, label.Prefixes);
            _writer.Write(pointy ? "]: <" : "]: ");
            _writer.WriteLines(destination, definition.Destination.LinePrefixes);
            _writer.Write(pointy ? ">" : string.Empty);
            WriteGeneratedTitle(definition.Title);
            return;
        }

        WriteSyntaxText(syntax!.Opening);
        _writer.WriteLines(label.Text, label.Prefixes);
        WriteSyntaxText(syntax.AfterLabel);
        _writer.WriteLines(destination, definition.Destination.LinePrefixes);
        // The recorded title delimiters stay while the title does not now contain the closing one.
        char opening = syntax.AfterDestination.Text.Length > 0 ? syntax.AfterDestination.Text[^1] : '"';
        char closingDelimiter = opening == '(' ? ')' : opening;
        if (syntax.HasTitle == hasTitle && !(hasTitle && ContainsUnescaped(definition.Title.Text, closingDelimiter)))
        {
            WriteSyntaxText(syntax.AfterDestination);
            _writer.WriteLines(definition.Title.Text, definition.Title.LinePrefixes);
            WriteSyntaxText(syntax.AfterTitle);
            return;
        }

        _writer.Write(syntax.IsPointy ? ">" : string.Empty);
        WriteGeneratedTitle(definition.Title);
    }

    /// <summary>
    /// A generated title is quoted with a delimiter the text does not contain: '"', then '\'', then
    /// parentheses. The slot is plain text (Q44), so with all three present the text cannot be
    /// written as a title as it is.
    /// </summary>
    // TODO(spec): a title holding '"', '\'' and ')' would need escaping, which the plain-text slot does not have.
    private void WriteGeneratedTitle(DefinitionSlot title)
    {
        if (title.Text.Length == 0)
        {
            return;
        }

        var (open, close) = !ContainsUnescaped(title.Text, '"') ? ('"', '"')
            : !ContainsUnescaped(title.Text, '\'') ? ('\'', '\'')
            : ('(', ')');
        _writer.Write(" " + open);
        _writer.WriteLines(title.Text, title.LinePrefixes);
        _writer.Write(close.ToString());
    }

    private void WriteSyntaxText(SyntaxText text) => _writer.WriteLines(text.Text, text.Prefixes);

    /// <summary>Whether the text holds the character not escaped by a backslash (titles keep their escapes as written).</summary>
    private static bool ContainsUnescaped(string text, char c)
    {
        for (int i = text.IndexOf(c); i >= 0; i = text.IndexOf(c, i + 1))
        {
            if (!Transactions.Normalizer.IsEscaped(text, i))
            {
                return true;
            }
        }

        return false;
    }

    private static bool NeedsAngleBrackets(string destination)
    {
        return destination.Length == 0 || destination.AsSpan().IndexOfAny(" \t\n") >= 0;
    }
}
