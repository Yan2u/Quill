using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Structural edits of a table (Q76–Q78, 04): each rebuilds the table block once, keeping the IDs
/// of the cells that stay, so it is one step and one undo item. The caret stays in its cell where
/// that cell survives, and otherwise goes to the nearest cell (04〔默认〕).
/// </summary>
internal static class TableEdits
{
    public static void Apply(TransactionBuilder builder, BlockId cellId, int offset, TableEdit edit)
    {
        var (table, row, column) = Locate(builder, cellId);
        var rows = Rows(table);
        var alignments = table.Alignments.ToList();
        BlockId? caret = cellId;
        int caretOffset = offset;
        switch (edit)
        {
            case TableEdit.InsertRowAbove or TableEdit.InsertRowBelow:
            {
                int at = edit == TableEdit.InsertRowAbove ? row : row + 1;
                rows.Insert(at, NewRow(builder, alignments.Count));
                (caret, caretOffset) = (rows[at].Cells[column].Id, 0);
                break;
            }

            case TableEdit.InsertColumnLeft or TableEdit.InsertColumnRight:
            {
                int at = edit == TableEdit.InsertColumnLeft ? column : column + 1;
                foreach (var (_, cells) in rows)
                {
                    cells.Insert(at, NewCell(builder));
                }

                alignments.Insert(at, TableAlignment.None);
                (caret, caretOffset) = (rows[row].Cells[at].Id, 0);
                break;
            }

            case TableEdit.MoveRowUp or TableEdit.MoveRowDown:
            {
                int to = row + (edit == TableEdit.MoveRowUp ? -1 : 1);
                if (to < 0 || to >= rows.Count)
                {
                    return;
                }

                (rows[row], rows[to]) = (rows[to], rows[row]);
                break;
            }

            case TableEdit.MoveColumnLeft or TableEdit.MoveColumnRight:
            {
                int to = column + (edit == TableEdit.MoveColumnLeft ? -1 : 1);
                if (to < 0 || to >= alignments.Count)
                {
                    return;
                }

                foreach (var (_, cells) in rows)
                {
                    (cells[column], cells[to]) = (cells[to], cells[column]);
                }

                (alignments[column], alignments[to]) = (alignments[to], alignments[column]);
                break;
            }

            case TableEdit.DeleteRow when rows.Count > 1:
                rows.RemoveAt(row);
                (caret, caretOffset) = (rows[Math.Min(row, rows.Count - 1)].Cells[column].Id, 0);
                break;
            case TableEdit.DeleteColumn when alignments.Count > 1:
                foreach (var (_, cells) in rows)
                {
                    cells.RemoveAt(column);
                }

                alignments.RemoveAt(column);
                (caret, caretOffset) = (rows[row].Cells[Math.Min(column, alignments.Count - 1)].Id, 0);
                break;
            case TableEdit.DeleteRow or TableEdit.DeleteColumn or TableEdit.DeleteTable:
                // The last row or column goes with the table (04〔默认〕).
                DeleteTable(builder, table);
                return;
            case TableEdit.FormatSource:
                builder.ReplaceBlock(Format(table));
                builder.SetCaret(cellId, offset);
                return;
        }

        builder.ReplaceBlock(Rebuild(builder, table, rows, alignments));
        builder.SetCaret(caret.Value, caretOffset);
    }

    /// <summary>Sets the caret column's alignment; asking for the current one resets it.</summary>
    public static void Align(TransactionBuilder builder, BlockId cellId, int offset, TableAlignment alignment)
    {
        var (table, _, column) = Locate(builder, cellId);
        var alignments = table.Alignments.ToList();
        alignments[column] = alignments[column] == alignment ? TableAlignment.None : alignment;
        builder.ReplaceBlock(table with { Alignments = [.. alignments] });
        builder.SetCaret(cellId, offset);
    }

    /// <summary>Resizes the table; cut rows and columns go with their content (Q78).</summary>
    public static void Resize(TransactionBuilder builder, BlockId cellId, int offset, int columns, int rowCount)
    {
        columns = Math.Max(1, columns);
        rowCount = Math.Max(1, rowCount);
        var (table, row, column) = Locate(builder, cellId);
        var rows = Rows(table).Take(rowCount).ToList();
        while (rows.Count < rowCount)
        {
            rows.Add(NewRow(builder, table.ColumnCount));
        }

        foreach (var (_, cells) in rows)
        {
            if (cells.Count > columns)
            {
                cells.RemoveRange(columns, cells.Count - columns);
            }

            while (cells.Count < columns)
            {
                cells.Add(NewCell(builder));
            }
        }

        var alignments = table.Alignments.Take(columns).ToList();
        while (alignments.Count < columns)
        {
            alignments.Add(TableAlignment.None);
        }

        builder.ReplaceBlock(Rebuild(builder, table, rows, alignments));
        bool kept = row < rowCount && column < columns;
        builder.SetCaret(kept ? cellId : rows[Math.Min(row, rowCount - 1)].Cells[Math.Min(column, columns - 1)].Id, kept ? offset : 0);
    }

    /// <summary>The Markdown source of the table at a cell, for "copy table".</summary>
    public static Block TableOf(Document document, BlockId cellId) =>
        DocumentTree.Locate(document, cellId).ContainersInnermostFirst().OfType<Table>().First();

    /// <summary>The display width of text in columns: wide East Asian characters count two.</summary>
    public static int Width(string text)
    {
        int width = 0;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            int code = char.ConvertToUtf32(elements.GetTextElement(), 0);
            width += code is >= 0x1100 and <= 0x115F or >= 0x2E80 and <= 0xA4CF or >= 0xAC00 and <= 0xD7A3 or >= 0xF900 and <= 0xFAFF
                or >= 0xFE30 and <= 0xFE4F or >= 0xFF00 and <= 0xFF60 or >= 0xFFE0 and <= 0xFFE6 or >= 0x1F300 and <= 0x1FAFF or >= 0x20000 and <= 0x3FFFD ? 2 : 1;
        }

        return width;
    }

    private static (Table Table, int Row, int Column) Locate(TransactionBuilder builder, BlockId cellId)
    {
        var location = builder.Locate(cellId);
        var (tableBlock, row) = location.Ancestors[^2];
        return ((Table)tableBlock, row, location.Index);
    }

    /// <summary>The rows with their cells; a row keeps its node (and source syntax) while its cells do not change.</summary>
    private static List<(TableRow? Row, List<Block> Cells)> Rows(Table table) =>
        table.Children.Select(static row => ((TableRow?)row, ((TableRow)row).Children.ToList())).ToList();

    private static Table Rebuild(TransactionBuilder builder, Table table, List<(TableRow? Row, List<Block> Cells)> rows, List<TableAlignment> alignments)
    {
        var rebuilt = rows.Select(entry => (Block)(entry.Row switch
        {
            { } row when row.Children.SequenceEqual(entry.Cells) => row,
            // Changed cells: the row's written syntax no longer fits (a header row may not be padded).
            { } row => row with { Children = [.. entry.Cells], Syntax = null },
            null => new TableRow { Id = builder.NewId(), Children = [.. entry.Cells] },
        })).ToList();
        return table with { Children = [.. rebuilt], Alignments = [.. alignments] };
    }

    private static (TableRow? Row, List<Block> Cells) NewRow(TransactionBuilder builder, int columns)
    {
        var cells = new List<Block>(columns);
        for (int i = 0; i < columns; i++)
        {
            cells.Add(NewCell(builder));
        }

        return (null, cells);
    }

    /// <summary>A new empty table: <paramref name="rows"/> rows (the header row included) of <paramref name="columns"/> cells, no alignment.</summary>
    public static Table NewTable(TransactionBuilder builder, int columns, int rows)
    {
        var tableRows = new List<Block>(rows);
        for (int i = 0; i < rows; i++)
        {
            tableRows.Add(new TableRow { Id = builder.NewId(), Children = [.. NewRow(builder, columns).Cells] });
        }

        return new Table { Id = builder.NewId(), Children = [.. tableRows], Alignments = [.. Enumerable.Repeat(TableAlignment.None, columns)] };
    }

    private static TableCell NewCell(TransactionBuilder builder) => new() { Id = builder.NewId(), Text = string.Empty };

    /// <summary>Pads every column to its widest cell and aligns the delimiter row (only the syntax changes).</summary>
    private static Table Format(Table table)
    {
        var rows = table.Children.Cast<TableRow>().ToList();
        var widths = Enumerable.Range(0, table.ColumnCount)
            .Select(column => Math.Max(3, rows.Max(row => column < row.Children.Count ? Width(((TableCell)row.Children[column]).Text) : 0)))
            .ToList();
        var formatted = rows.Select(row =>
        {
            var gaps = new List<string> { "| " };
            for (int column = 0; column < row.Children.Count; column++)
            {
                int pad = (column < widths.Count ? widths[column] : 0) - Width(((TableCell)row.Children[column]).Text);
                gaps.Add(new string(' ', Math.Max(0, pad)) + (column == row.Children.Count - 1 ? " |" : " | "));
            }

            return (Block)(row with
            {
                Syntax = new TableRowSyntax { Prefix = row.Syntax?.Prefix ?? LinePrefix.None, Gaps = [.. gaps], Cells = [.. row.Children.Select(static cell => cell.Id)] },
            });
        }).ToList();
        var delimiter = new StringBuilder("|");
        for (int column = 0; column < table.ColumnCount; column++)
        {
            var alignment = table.Alignments[column];
            int dashes = widths[column] - (alignment is TableAlignment.Left or TableAlignment.Right ? 1 : alignment == TableAlignment.Center ? 2 : 0);
            delimiter.Append(' ')
                .Append(alignment is TableAlignment.Left or TableAlignment.Center ? ":" : string.Empty)
                .Append('-', Math.Max(1, dashes))
                .Append(alignment is TableAlignment.Right or TableAlignment.Center ? ":" : string.Empty)
                .Append(" |");
        }

        return table with
        {
            Children = [.. formatted],
            Syntax = new TableSyntax { DelimiterRow = new SyntaxLine(delimiter.ToString(), table.Syntax?.DelimiterRow.Prefix ?? LinePrefix.None), Alignments = table.Alignments },
        };
    }

    /// <summary>Removes the table; the caret goes to the next text block, else the previous one, else the gap (04〔默认〕).</summary>
    private static void DeleteTable(TransactionBuilder builder, Table table)
    {
        var location = builder.Locate(table.Id);
        var parent = location.Parent!;
        int index = location.Index;
        builder.RemoveBlocks(parent.Id, index, 1);
        var container = builder.Find<ContainerBlock>(parent.Id);
        if (index < container.Children.Count && !BlockRules.IsNonText(container.Children[index], atStart: true)
            && Navigation.First(builder.Document, container.Children[index]) is TextPosition next)
        {
            builder.SetCaret(next);
        }
        else if (index > 0 && Navigation.Last(builder.Document, container.Children[index - 1]) is TextPosition previous)
        {
            builder.SetCaret(previous);
        }
        else
        {
            builder.SetCaret(new BoundaryPosition(parent.Id, index));
        }
    }
}
