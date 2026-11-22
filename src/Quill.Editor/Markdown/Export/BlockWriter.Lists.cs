using System.Globalization;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Export;

internal sealed partial class BlockWriter
{
    /// <summary>Columns of marker and spacing of the list items being written, innermost on top.</summary>
    private readonly Stack<int> _markerWidths = new();

    /// <summary>
    /// Lists (02-document-model.md). A list is loose when it was loose on import or holds a headless
    /// item anywhere in its tree (<see cref="ListRules.IsLoose"/>). When that differs from how it was
    /// imported, every separator in the list (sub-lists included) is regenerated; otherwise recorded
    /// separators are kept and only new ones follow the looseness, which also applies to sub-lists.
    /// </summary>
    /// <param name="parentItem">The list item the list is a direct child of, if any.</param>
    private void WriteList(ListBlock list, BodyContext context, char marker, ListItem? parentItem)
    {
        bool loose = ListRules.IsLoose(list);
        bool changed = list.Syntax is null || list.Syntax.WasLoose != loose;
        var itemContext = new BodyContext(InListItem: true, Loose: loose || context.Loose, Regenerate: context.Regenerate || changed);

        // Generated sub-list markers sit four columns right of the parent's marker, or at the parent's
        // content column when that is further (Q17). Next to written items, they take the written
        // items' indentation: siblings indented differently would nest.
        string nestedIndent = parentItem is not null && _markerWidths.Count > 0
            ? new string(' ', Math.Max(0, 4 - _markerWidths.Peek()))
            : string.Empty;
        if (parentItem is not null && ReferenceEquals(parentItem.Children[0], list))
        {
            // A list that opens its item continues the item's marker line: "- - a" (Q48).
            nestedIndent = string.Empty;
        }

        if (Columns(nestedIndent) >= _absorbedFrom)
        {
            // Right after another list, an indentation that deep would nest into its last item.
            nestedIndent = string.Empty;
        }

        nestedIndent = list.Children.Select(static child => ((ListItem)child).Syntax?.Indent).FirstOrDefault(static indent => indent is not null) ?? nestedIndent;
        int firstNumber = (list.Children.Count > 0 ? ((ListItem)list.Children[0]).Number : null) ?? 1;
        int absorbedFrom = _absorbedFrom;
        int contentColumns = 0;
        int maxIndentColumns = int.MaxValue;
        for (int index = 0; index < list.Children.Count; index++)
        {
            var item = (ListItem)list.Children[index];
            if (index > 0)
            {
                WriteSeparator(item, list.Children[index - 1], itemContext, force: false);
            }

            // A list right after another must not start inside its last item. Later items line up with
            // where the first one went (it may have lost its indentation on its parent's marker line):
            // an item indented to the previous item's content would nest in it.
            _absorbedFrom = absorbedFrom;
            (string indent, contentColumns) = WriteListItem(item, list, firstNumber + index, marker, nestedIndent, itemContext, maxIndentColumns);
            absorbedFrom = int.MaxValue;
            maxIndentColumns = contentColumns - 1;
            if (index == 0)
            {
                nestedIndent = indent;
            }
        }

        // An empty last item does not end at a blank line after it either (Typora; see QuillListBlockParser).
        _lastItemContentColumns = contentColumns;
    }

    /// <param name="maxIndentColumns">The deepest indentation the item may have; a recorded deeper one gives way to <paramref name="nestedIndent"/>.</param>
    /// <returns>The indentation written before the marker and the item's content column, relative to the list's container.</returns>
    private (string Indent, int ContentColumns) WriteListItem(ListItem item, ListBlock list, int fallbackNumber, char marker, string nestedIndent, BodyContext context, int maxIndentColumns)
    {
        var syntax = item.Syntax;
        string generatedMarker = list.IsOrdered
            ? (item.Number ?? fallbackNumber).ToString(CultureInfo.InvariantCulture) + marker
            : marker.ToString();

        string? storedOpening = null;
        bool reuseContinuations = false;
        string writtenIndent = nestedIndent;
        int indentColumns = nestedIndent.Length;
        int contentColumns = indentColumns + generatedMarker.Length + 1;

        // An item with empty text and more blocks opens with a blank line: its marker line holds only
        // the marker and each empty paragraph is a blank line after it, which reads back the same way
        // (Typora keeps the blocks of an emptied item). Otherwise an item opens with a blank line as
        // it was written.
        bool emptyText = item.Task == TaskState.None && item.Children is [Paragraph { IsEmpty: true }, ..];
        bool recordedBlankOpening = syntax is { OpensWithBlankLine: true };
        bool blankOpening = emptyText ? item.Children.Count > 1 : recordedBlankOpening;
        if (syntax is not null)
        {
            // A bare marker that gets content needs a space; an item emptied by editing is written with
            // one space after its marker, like a new empty item (wider spacing would become its text).
            string current = CurrentMarker(syntax.Marker, item, list.IsOrdered, marker);
            bool emptied = item.Task == TaskState.None && item.Children is [Paragraph { Text.Length: 0 }, ..];
            bool bare = syntax.Spacing.Length == 0 && !blankOpening && !emptied;
            // Indented code on the marker line follows one column of spacing: wider spacing would be
            // part of the code (CommonMark). (A recorded tab there stands for one column of it.)
            bool codeOpens = !blankOpening && item.Children is [CodeFence { Syntax.Style: CodeBlockStyle.Indented, Info.Length: 0 }, ..]
                && syntax.Spacing.Length > 1 && syntax.Spacing.AsSpan().Trim(' ').IsEmpty;
            string spacing = bare || codeOpens || blankOpening != recordedBlankOpening || (emptied && syntax.Spacing.Length > 1 && !blankOpening)
                ? " "
                : syntax.Spacing;
            string indent = Unabsorbed(syntax.Indent);
            if (!indent.Contains('\t') && _footnoteDepth == 0 && Columns(indent) > maxIndentColumns)
            {
                // (A tab's width depends on the column it starts at, which is not known here; in a
                // footnote Markdig takes lines without the footnote's indentation, so columns do not compare.)
                indent = nestedIndent;
            }

            writtenIndent = indent;
            storedOpening = indent + current + spacing;
            reuseContinuations = current.Length == syntax.Marker.Length;
            indentColumns = Columns(indent);
            contentColumns = blankOpening
                ? Columns(indent + current) + 1
                : Columns(storedOpening);
        }

        var scope = new ListItemScope(item.Id, storedOpening, nestedIndent + generatedMarker, marker, reuseContinuations, contentColumns);
        _writer.MarkBlockStart(item.Id);
        _writer.PushScope(scope);
        _markerWidths.Push(contentColumns - indentColumns);
        if (blankOpening)
        {
            _writer.BeginLine(recordedBlankOpening ? syntax!.OpeningLinePrefix : LinePrefix.None);
        }

        _absorbedFrom = int.MaxValue;
        WriteBody(item.Children, context, item, previousIsContent: false);
        _markerWidths.Pop();
        _writer.PopScope();
        return (writtenIndent, contentColumns);
    }

    /// <summary>
    /// The marker as written, with the number rewritten when the item's number changed and the
    /// character switched when the list's marker character changed.
    /// </summary>
    private static string CurrentMarker(string written, ListItem item, bool ordered, char marker)
    {
        if (!ordered)
        {
            return written == marker.ToString() ? written : marker.ToString();
        }

        string digits = written.TrimEnd('.', ')');
        string current = written;
        if (item.Number is { } number
            && (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int writtenNumber) || writtenNumber != number))
        {
            current = number.ToString(CultureInfo.InvariantCulture) + (written.Length > 0 ? written[^1] : marker);
        }

        return current.Length > 0 && current[^1] != marker ? current[..^1] + marker : current;
    }

    /// <summary>Display columns of text starting at column 0, with tab stops of 4.</summary>
    internal static int Columns(string text)
    {
        int column = 0;
        foreach (char c in text)
        {
            column += c == '\t' ? 4 - (column % 4) : 1;
        }

        return column;
    }
}
