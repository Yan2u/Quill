using System.Collections.Immutable;
using Quill.Editor.Behaviors;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// In-table multi-cell paste operations (Q122): overwrites cells starting from the caret/selection
/// origin, expands rows and columns automatically when the pasted grid overflows, or replaces the
/// whole table when the entire table is selected, all within one undoable transaction.
/// </summary>
internal static class TablePaste
{
    public static bool IsInsideSingleTable(Document document, TextSelection selection, out Table table)
    {
        table = null!;
        var (start, end) = PositionOrder.Ordered(document, selection);
        if (start is not TextPosition s || end is not TextPosition e
            || DocumentTree.TryFind(document, s.Block) is not TableCell
            || DocumentTree.TryFind(document, e.Block) is not TableCell)
        {
            return false;
        }

        var startLocation = DocumentTree.Locate(document, s.Block);
        var endLocation = DocumentTree.Locate(document, e.Block);
        if (startLocation.Ancestors.Length < 2 || endLocation.Ancestors.Length < 2)
        {
            return false;
        }

        var startTable = startLocation.Ancestors[^2].Container;
        var endTable = endLocation.Ancestors[^2].Container;
        if (startTable.Id != endTable.Id || startTable is not Table resolved)
        {
            return false;
        }

        table = resolved;
        return true;
    }

    public static bool IsWholeTableSelected(Document document, TextSelection selection, out Table table)
    {
        table = null!;
        if (selection.IsCollapsed || !IsInsideSingleTable(document, selection, out table))
        {
            return false;
        }

        var (start, end) = PositionOrder.Ordered(document, selection);
        var firstCell = (TableCell)((TableRow)table.Children[0]).Children[0];
        var lastCell = (TableCell)((TableRow)table.Children[^1]).Children[^1];
        return start is TextPosition s && end is TextPosition e
            && s.Block == firstCell.Id && s.Offset == 0
            && e.Block == lastCell.Id && e.Offset == lastCell.Text.Length;
    }

    public static Transaction? PasteIntoTable(EditorState state, TableGrid grid, BehaviorContext context)
    {
        if (grid.IsEmpty || grid.IsSingleCell || !IsInsideSingleTable(state.Document, state.Selection, out var selectedTable))
        {
            return null;
        }

        var builder = context.Begin(state);
        if (IsWholeTableSelected(state.Document, state.Selection, out var wholeTable))
        {
            var replaced = grid.ToTable(builder.Ids) with
            {
                Id = wholeTable.Id,
                SeparatorBefore = wholeTable.SeparatorBefore,
            };
            builder.ReplaceBlock(replaced);
            var endCell = (TableCell)((TableRow)replaced.Children[^1]).Children[^1];
            builder.SetCaret(endCell.Id, endCell.Text.Length);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        EditOperations.DeleteSelection(builder);
        if (builder.Selection.Head is not TextPosition caret || builder.Find<Block>(caret.Block) is not TableCell)
        {
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        var caretLocation = builder.Locate(caret.Block);
        var (tableBlock, startRow) = caretLocation.Ancestors[^2];
        var table = (Table)tableBlock;
        int startCol = caretLocation.Index;

        int targetRows = Math.Max(table.Children.Count, startRow + grid.RowCount);
        int targetCols = Math.Max(table.ColumnCount, startCol + grid.ColumnCount);

        var rebuiltRows = ImmutableList.CreateBuilder<Block>();
        for (int r = 0; r < targetRows; r++)
        {
            bool existingRow = r < table.Children.Count;
            var sourceRow = existingRow ? (TableRow)table.Children[r] : null;
            bool rowTouched = !existingRow || targetCols != table.ColumnCount || (r >= startRow && r < startRow + grid.RowCount);

            if (!rowTouched && sourceRow is not null)
            {
                rebuiltRows.Add(sourceRow);
                continue;
            }

            var cells = ImmutableList.CreateBuilder<Block>();
            for (int c = 0; c < targetCols; c++)
            {
                TableCell? existingCell = sourceRow is not null && c < sourceRow.Children.Count
                    ? (TableCell)sourceRow.Children[c]
                    : null;

                int dr = r - startRow;
                int dc = c - startCol;
                if (dr >= 0 && dr < grid.RowCount && dc >= 0 && dc < grid.ColumnCount)
                {
                    string pastedText = grid.Rows[dr][dc];
                    cells.Add(existingCell is not null
                        ? existingCell with { Text = pastedText, LinePrefixes = [] }
                        : new TableCell { Id = builder.NewId(), Text = pastedText });
                }
                else
                {
                    cells.Add(existingCell ?? new TableCell { Id = builder.NewId(), Text = string.Empty });
                }
            }

            rebuiltRows.Add(sourceRow is not null
                ? sourceRow with { Children = cells.ToImmutable(), Syntax = null }
                : new TableRow { Id = builder.NewId(), Children = cells.ToImmutable() });
        }

        var alignments = table.Alignments;
        if (targetCols > table.ColumnCount)
        {
            var alignBuilder = ImmutableArray.CreateBuilder<TableAlignment>(targetCols);
            alignBuilder.AddRange(table.Alignments);
            for (int c = table.ColumnCount; c < targetCols; c++)
            {
                int dc = c - startCol;
                alignBuilder.Add(dc >= 0 && dc < grid.Alignments.Length ? grid.Alignments[dc] : TableAlignment.None);
            }

            alignments = alignBuilder.MoveToImmutable();
        }

        var rebuiltTable = table with
        {
            Children = rebuiltRows.ToImmutable(),
            Alignments = alignments,
            Syntax = targetCols == table.ColumnCount ? table.Syntax : null,
        };
        builder.ReplaceBlock(rebuiltTable);

        int finalRow = startRow + grid.RowCount - 1;
        int finalCol = startCol + grid.ColumnCount - 1;
        var finalCell = (TableCell)((TableRow)rebuiltTable.Children[finalRow]).Children[finalCol];
        builder.SetCaret(finalCell.Id, finalCell.Text.Length);
        return builder.Build(TransactionKind.Edit, context.Now);
    }
}
