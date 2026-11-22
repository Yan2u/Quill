using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Editing.Lists;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// List items (04 behavior catalog, Typora list rules), defined on the row view of
/// 02-document-model.md: Tab and Shift+Tab change the level of a row group, Enter splits an item or
/// leaves the list, Backspace at the start of an item makes it headless or merges it, Delete at the end
/// merges the next row. Soft breaks, typing and Ctrl+Enter (a plain paragraph split, which adds a
/// headless item) are generic.
/// </summary>
internal sealed class ListItemBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || command is not (SplitBlock or DeleteBackward or DeleteForward or Indent or Outdent))
        {
            return null;
        }

        var row = RowContext.Find(state.Document, head.Block);
        if (row is null)
        {
            return null;
        }

        bool collapsed = state.Selection.IsCollapsed;
        var leaf = DocumentTree.Find(state.Document, head.Block) as LeafBlock;
        return command switch
        {
            Indent when row.Value.IsItemRow => IndentRow(state, row.Value, context),
            Outdent => OutdentRow(state, row.Value, context),
            SplitBlock when row.Value.IsDirect && leaf is Paragraph => Enter(state, context),
            DeleteBackward when collapsed && row.Value.IsDirect && leaf is Paragraph && head.Offset == 0 => Backspace(state, row.Value, context),
            DeleteForward when collapsed && row.Value.IsDirect && leaf is Paragraph or Heading && head.Offset == leaf.Text.Length => Delete(state, (LeafBlock)leaf!, row.Value, context),
            _ => null,
        };
    }

    /// <summary>Tab: the item row and its group go one level deeper when the row can become a child; otherwise a tab is typed.</summary>
    private static Transaction? IndentRow(EditorState state, RowContext row, BehaviorContext context)
    {
        var view = ListRowView.ForBlock(state.Document, row.RowBlock);
        int index = view?.IndexOf(row.RowBlock) ?? -1;
        if (view is null || index < 0 || !view.CanIndent(index))
        {
            return null;
        }

        var builder = context.Begin(state);
        view.ShiftGroup(index, 1);
        view.WriteBack(builder);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Shift+Tab: the row and its group go one level up; at level 1 the row leaves the list (Q21).</summary>
    private static Transaction? OutdentRow(EditorState state, RowContext row, BehaviorContext context)
    {
        var view = ListRowView.ForBlock(state.Document, row.RowBlock);
        int index = view?.IndexOf(row.RowBlock) ?? -1;
        if (view is null || index < 0)
        {
            return null;
        }

        var builder = context.Begin(state);
        view.ShiftGroup(index, -1);
        view.WriteBack(builder);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Enter in a paragraph that is a row. Item row: an empty one leaves the list at level 1 and
    /// becomes a headless item of the level above otherwise; a non-empty one splits into a new item
    /// with the same task state. Headless row: an empty one becomes an item again; a non-empty one
    /// splits into another headless item.
    /// </summary>
    private static Transaction? Enter(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        if (builder.Selection.Head is not TextPosition caret || builder.Find<Block>(caret.Block) is not Paragraph paragraph)
        {
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (RowContext.Find(builder.Document, caret.Block) is not { IsDirect: true } row || (!row.IsItemRow && paragraph.Text.Length > 0))
        {
            // A headless item splits like a paragraph, which adds another headless item.
            var tail = EditOperations.SplitLeaf(builder, paragraph.Id, caret.Offset, static (id, text) => new Paragraph { Id = id, Text = text });
            builder.SetCaret(tail, 0);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        string rest = paragraph.Text[caret.Offset..];
        if (row.IsItemRow && paragraph.Text.Length > 0)
        {
            builder.ReplaceText(paragraph.Id, caret.Offset, paragraph.Text.Length, string.Empty);
        }

        var view = ListRowView.ForBlock(builder.Document, row.RowBlock);
        int index = view?.IndexOf(row.RowBlock) ?? -1;
        if (view is null || index < 0)
        {
            return null;
        }

        var current = view.Rows[index];
        if (!row.IsItemRow)
        {
            // An empty headless item becomes an item again, with the task state of its item.
            view.Rows[index] = current with { Kind = ListRowKind.Item, NewTask = OwnerTask(view, index) };
        }
        else if (paragraph.Text.Length == 0)
        {
            if (current.Level > 1)
            {
                view.Rows[index] = current with { Kind = ListRowKind.Content };
            }

            view.ShiftGroup(index, -1);
        }
        else
        {
            var split = new Paragraph { Id = builder.NewId(), Text = rest };
            view.Rows.Insert(index + 1, new ListRow
            {
                Block = split,
                Kind = ListRowKind.Item,
                Level = current.Level,
                NewTask = current.Task,
                OriginalList = current.OriginalList,
            });
            view.WriteBack(builder);
            builder.SetCaret(split.Id, 0);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        view.WriteBack(builder);
        builder.SetCaret(paragraph.Id, 0);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Backspace at the start of a row. An item row that is the first of its list, or follows an
    /// empty row, is removed and its text merged into the previous row (with no previous row it
    /// leaves the list as a paragraph); any other item row becomes a headless item. A headless row
    /// merges into the previous row.
    /// </summary>
    private static Transaction? Backspace(EditorState state, RowContext row, BehaviorContext context)
    {
        var view = ListRowView.ForBlock(state.Document, row.RowBlock);
        int index = view?.IndexOf(row.RowBlock) ?? -1;
        if (view is null || index < 0)
        {
            return null;
        }

        var builder = context.Begin(state);
        if (row.IsItemRow && index > 0 && view.Rows[index - 1].IsBare)
        {
            // "- - |a": the inner item goes and its content takes the outer item's place〔默认〕.
            var bare = view.Rows[index - 1];
            view.Rows[index] = view.Rows[index] with { Item = bare.Item, NewTask = bare.Task, OriginalList = bare.OriginalList, OriginalParent = bare.OriginalParent };
            view.ShiftGroup(index, -1);
            view.Rows.RemoveAt(index - 1);
            view.WriteBack(builder);
            builder.SetCaret(row.RowBlock, 0);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (row.IsItemRow)
        {
            bool merge = view.IsFirstItemOfList(index)
                || (index > 0 && view.Rows[index - 1].Block is Paragraph { Text.Length: 0 });
            if (!merge)
            {
                view.Rows[index] = view.Rows[index] with { Kind = ListRowKind.Content };
                view.WriteBack(builder);
                builder.SetCaret(row.RowBlock, 0);
                return builder.Build(TransactionKind.Edit, context.Now);
            }

            if (index == 0)
            {
                view.ShiftGroup(index, -1);
                view.WriteBack(builder);
                builder.SetCaret(row.RowBlock, 0);
                return builder.Build(TransactionKind.Edit, context.Now);
            }
        }

        return MergeRows(state, builder, view, index - 1, index, context);
    }

    /// <summary>
    /// Delete at the end of a row: the next row merges into it (a row without text is selected whole
    /// first, Q36); after the last row, the block after the list does.
    /// </summary>
    private static Transaction? Delete(EditorState state, LeafBlock leaf, RowContext row, BehaviorContext context)
    {
        var view = ListRowView.ForBlock(state.Document, row.RowBlock);
        int index = view?.IndexOf(row.RowBlock) ?? -1;
        if (view is null || index < 0)
        {
            return null;
        }

        var builder = context.Begin(state);
        if (index + 1 < view.Rows.Count)
        {
            return MergeRows(state, builder, view, index, index + 1, context);
        }

        var parent = builder.Find<ContainerBlock>(view.Parent);
        if (view.Index + 1 >= parent.Children.Count)
        {
            return null;
        }

        var next = parent.Children[view.Index + 1];
        if (next is Paragraph or Heading)
        {
            int junction = leaf.Text.Length;
            builder.InsertText(leaf.Id, junction, TextBehavior.AdaptText(leaf, ((LeafBlock)next).Text));
            builder.RemoveBlocks(parent.Id, view.Index + 1, 1);
            builder.SetCaret(leaf.Id, junction);
        }
        else
        {
            builder.Selection = BoundaryEdits.WholeBlock(parent, view.Index + 1);
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Merges the text of row <paramref name="source"/> into the last text leaf of row
    /// <paramref name="target"/> and removes the source row. When the source row was the first child
    /// row under the target, the rest of its group moves up a level, so that its headless items and
    /// sub-lists join the target's item (02: "父列表项和上一项合并时，子列表转移到新的父列表项").
    /// A target without text, or a source that is not a paragraph or heading, is selected whole
    /// instead (Q36).
    /// </summary>
    private static Transaction? MergeRows(EditorState state, TransactionBuilder builder, ListRowView view, int target, int source, BehaviorContext context)
    {
        // A bare row has no text: the first row of its sub-list is the one that merges.
        while (source < view.Rows.Count && view.Rows[source].IsBare)
        {
            source++;
        }

        if (source >= view.Rows.Count || view.Rows[target].IsBare)
        {
            return NoChange.For(state);
        }

        var targetLeaf = BoundaryEdits.MergeTarget(view.Rows[target].Block!);
        if (targetLeaf is null || view.Rows[source].Block is not (Paragraph or Heading) || view.Rows[source].Block is not LeafBlock sourceLeaf)
        {
            var location = builder.Locate(view.Rows[targetLeaf is null ? target : source].Block!.Id);
            builder.Selection = BoundaryEdits.WholeBlock(location.Parent!, location.Index);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        int junction = targetLeaf.Text.Length;
        builder.InsertText(targetLeaf.Id, junction, TextBehavior.AdaptText(targetLeaf, sourceLeaf.Text));

        // Rebuild the view on the edited document so that its rows hold the current blocks.
        var rebuilt = ListRowView.ForBlock(builder.Document, sourceLeaf.Id)!;
        int sourceLevel = rebuilt.Rows[source].Level;
        int targetLevel = rebuilt.Rows[target].Level;
        int end = rebuilt.GroupEnd(source);
        if (targetLevel < sourceLevel)
        {
            for (int i = source + 1; i < end; i++)
            {
                rebuilt.Rows[i] = rebuilt.Rows[i] with { Level = rebuilt.Rows[i].Level - (sourceLevel - targetLevel), StartsList = false };
            }
        }

        rebuilt.Rows.RemoveAt(source);
        rebuilt.WriteBack(builder);
        builder.SetCaret(targetLeaf.Id, junction);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>The task state of the item a headless row belongs to.</summary>
    private static TaskState OwnerTask(ListRowView view, int index)
    {
        int level = view.Rows[index].Level;
        for (int i = index - 1; i >= 0 && view.Rows[i].Level >= level; i--)
        {
            if (view.Rows[i].Level == level && view.Rows[i].Kind == ListRowKind.Item)
            {
                return view.Rows[i].Task;
            }
        }

        return TaskState.None;
    }

    /// <summary>Where the head is relative to the innermost list item holding it.</summary>
    /// <param name="RowBlock">The child of the list item that holds the head: the row's block.</param>
    /// <param name="IsItemRow">Whether that child is the item's first child (an item row).</param>
    /// <param name="IsDirect">Whether the head's leaf is the row block itself.</param>
    private readonly record struct RowContext(BlockId RowBlock, bool IsItemRow, bool IsDirect)
    {
        public static RowContext? Find(Document document, BlockId leaf)
        {
            var ancestors = DocumentTree.Locate(document, leaf).Ancestors;
            for (int level = ancestors.Length - 1; level >= 0; level--)
            {
                if (ancestors[level].Container is ListItem item)
                {
                    var child = item.Children[ancestors[level].Index];
                    return child is ListBlock ? null : new RowContext(child.Id, ancestors[level].Index == 0, child.Id == leaf);
                }
            }

            return null;
        }
    }
}
