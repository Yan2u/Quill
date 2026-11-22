using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Table cells (04 behavior catalog). Enter moves to the cell of the same column in the next row, or
/// leaves the table after the last row; Tab and Shift+Tab move to the next and previous cell, Tab
/// adding a row after the last cell; Shift+Enter types &lt;br&gt; (through
/// <see cref="TextBehavior.AdaptText"/>); Backspace at the start and Delete at the end of a cell do
/// nothing. A moved caret goes to the start of the cell〔默认〕. Ctrl+Enter inserts a row below; the
/// structural edits of the toolbar and its menu (Q76–Q78) go to <see cref="TableEdits"/>.
/// </summary>
internal sealed class TableCellBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not TableCell cell)
        {
            return null;
        }

        bool collapsed = state.Selection.IsCollapsed;
        switch (command)
        {
            case SplitBlock:
                return Enter(state, context);
            case Indent:
                return MoveToCell(state, cell.Id, forward: true, context);
            case Outdent:
                return MoveToCell(state, cell.Id, forward: false, context) ?? NoChange.For(state);
            case DeleteBackward when collapsed && head.Offset == 0:
            case DeleteForward when collapsed && head.Offset == cell.Text.Length:
                return NoChange.For(state);
            case SplitBlockAlternate:
                return Structure(state, context, builder => TableEdits.Apply(builder, cell.Id, head.Offset, TableEdit.InsertRowBelow));
            case EditTable table:
                return Structure(state, context, builder => TableEdits.Apply(builder, cell.Id, head.Offset, table.Edit));
            case SetColumnAlignment align:
                return Structure(state, context, builder => TableEdits.Align(builder, cell.Id, head.Offset, align.Alignment));
            case ResizeTable resize:
                return Structure(state, context, builder => TableEdits.Resize(builder, cell.Id, head.Offset, resize.Columns, resize.Rows));
            default:
                return null;
        }
    }

    /// <summary>A structural edit: one transaction, one undo item; no change when the edit has nothing to do.</summary>
    private static Transaction Structure(EditorState state, BehaviorContext context, Action<TransactionBuilder> edit)
    {
        var builder = context.Begin(state);
        edit(builder);
        return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
    }

    private static Transaction? Enter(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        if (builder.Selection.Head is not TextPosition caret || builder.Find<Block>(caret.Block) is not TableCell)
        {
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        var cellLocation = builder.Locate(caret.Block);
        var (table, rowIndex) = cellLocation.Ancestors[^2];
        int column = cellLocation.Index;
        if (rowIndex + 1 < table.Children.Count)
        {
            builder.SetCaret(((TableRow)table.Children[rowIndex + 1]).Children[column].Id, 0);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        // Leave the table: to the text block after it, or into a new real paragraph (Q19).
        var (container, tableIndex) = cellLocation.Ancestors[^3];
        if (tableIndex + 1 < container.Children.Count && !BlockRules.IsNonText(container.Children[tableIndex + 1], atStart: true)
            && Navigation.First(builder.Document, container.Children[tableIndex + 1]) is TextPosition next)
        {
            builder.SetCaret(next);
        }
        else
        {
            var paragraph = EditOperations.NewParagraph(builder);
            builder.InsertBlocks(container.Id, tableIndex + 1, paragraph);
            builder.SetCaret(paragraph.Id, 0);
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Tab: the next cell, a new row after the last cell. Shift+Tab: the previous cell, null before the first.</summary>
    private static Transaction? MoveToCell(EditorState state, BlockId cellId, bool forward, BehaviorContext context)
    {
        var builder = context.Begin(state);
        var location = builder.Locate(cellId);
        var row = (TableRow)location.Parent!;
        var (table, rowIndex) = location.Ancestors[^2];
        int column = location.Index + (forward ? 1 : -1);
        if (column >= 0 && column < row.Children.Count)
        {
            builder.SetCaret(row.Children[column].Id, 0);
        }
        else if (forward && rowIndex + 1 < table.Children.Count)
        {
            builder.SetCaret(((TableRow)table.Children[rowIndex + 1]).Children[0].Id, 0);
        }
        else if (!forward && rowIndex > 0)
        {
            builder.SetCaret(((TableRow)table.Children[rowIndex - 1]).Children[^1].Id, 0);
        }
        else if (forward)
        {
            int columns = ((Table)table).ColumnCount;
            var cells = Enumerable.Range(0, columns).Select(_ => (Block)new TableCell { Id = builder.NewId(), Text = string.Empty }).ToList();
            var newRow = new TableRow { Id = builder.NewId(), Children = [.. cells] };
            builder.InsertBlocks(table.Id, table.Children.Count, newRow);
            builder.SetCaret(cells[0].Id, 0);
        }
        else
        {
            return null;
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }
}
