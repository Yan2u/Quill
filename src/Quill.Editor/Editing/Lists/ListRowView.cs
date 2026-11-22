using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing.Lists;

/// <summary>
/// The row view of a top-level list (02-document-model.md): every block of its items except sub-lists,
/// in document order, with a kind and a level. List behaviors change rows (their kind, their level,
/// rows inserted or removed) and <see cref="WriteBack"/> rebuilds the list structure from them.
/// </summary>
/// <remarks>
/// Building and writing back cost time proportional to the size of the top-level list; only list
/// structure commands (Enter, Backspace at an item start, Tab, Shift+Tab) use the view.
/// </remarks>
internal sealed class ListRowView
{
    /// <summary>Where each original list was: the block it was a child of and its level.</summary>
    private readonly Dictionary<BlockId, (BlockId Parent, int Level)> _lists;

    private ListRowView(ListBlock list, BlockId parent, int index, List<ListRow> rows, Dictionary<BlockId, (BlockId, int)> lists)
    {
        List = list;
        Parent = parent;
        Index = index;
        Rows = rows;
        _lists = lists;
    }

    /// <summary>The top-level list: a list that is not inside a list item.</summary>
    public ListBlock List { get; }

    /// <summary>The container of the top-level list.</summary>
    public BlockId Parent { get; }

    /// <summary>The index of the top-level list in its container.</summary>
    public int Index { get; }

    public List<ListRow> Rows { get; }

    /// <summary>
    /// The row view of the top-level list holding the block (or the list item itself), or null when the
    /// block is in no list item.
    /// </summary>
    public static ListRowView? ForBlock(Document document, BlockId block)
    {
        var location = DocumentTree.Locate(document, block);
        var ancestors = location.Ancestors;
        var chain = ancestors.Select(static step => step.Container).ToList();
        if (location.Block is ListItem self)
        {
            chain.Add(self);
        }

        int item = chain.FindLastIndex(static container => container is ListItem);
        if (item < 2)
        {
            return null;
        }

        // Climb item, list, item, list... up to a list whose container is not an item.
        while (item >= 4 && chain[item - 2] is ListItem)
        {
            item -= 2;
        }

        var list = (ListBlock)chain[item - 1];
        var (parent, index) = ancestors[item - 2];
        var rows = new List<ListRow>();
        var lists = new Dictionary<BlockId, (BlockId, int)>();
        AddRows(list, level: 1, parent.Id, startsList: false, rows, lists);
        return new ListRowView(list, parent.Id, index, rows, lists);
    }

    public int IndexOf(BlockId block) => Rows.FindIndex(row => row.Block?.Id == block);

    /// <summary>
    /// Rows for a list that is not in the document (pasted), its top level at <paramref name="level"/>.
    /// The caller inserts them into <see cref="Rows"/>.
    /// </summary>
    public List<ListRow> RowsOf(ListBlock list, int level)
    {
        var rows = new List<ListRow>();
        AddRows(list, level, parent: default, startsList: false, rows, _lists);
        return rows;
    }

    /// <summary>
    /// The end (exclusive) of the group of a row: the row and the rest of its list item, that is the
    /// following rows of a deeper level and the headless items of the same level up to the next item.
    /// Changing the level of a row changes its whole group (the descendants of 02-document-model.md,
    /// and the headless items that would otherwise lose their item).
    /// </summary>
    public int GroupEnd(int index)
    {
        int level = Rows[index].Level;
        int end = index + 1;
        while (end < Rows.Count && (Rows[end].Level > level || (Rows[end].Level == level && Rows[end].Kind == ListRowKind.Content)))
        {
            end++;
        }

        return end;
    }

    /// <summary>Whether the row can become a child: the nearest earlier row of its level or less has its level.</summary>
    public bool CanIndent(int index)
    {
        int level = Rows[index].Level;
        for (int i = index - 1; i >= 0; i--)
        {
            if (Rows[i].Level <= level)
            {
                return Rows[i].Level == level;
            }
        }

        return false;
    }

    /// <summary>Changes the level of the row's group by <paramref name="delta"/>.</summary>
    public void ShiftGroup(int index, int delta)
    {
        int end = GroupEnd(index);
        for (int i = index; i < end; i++)
        {
            Rows[i] = Rows[i] with { Level = Math.Max(0, Rows[i].Level + delta), StartsList = false };
        }
    }

    /// <summary>Whether the item row opens the first item of its list (no earlier item row of its level in the same list).</summary>
    public bool IsFirstItemOfList(int index)
    {
        var row = Rows[index];
        for (int i = index - 1; i >= 0; i--)
        {
            if (Rows[i].Level < row.Level)
            {
                return true;
            }

            if (Rows[i].Level == row.Level && Rows[i].Kind == ListRowKind.Item)
            {
                return row.StartsList;
            }
        }

        return true;
    }

    /// <summary>
    /// Replaces the top-level list with the structure the rows describe (02-document-model.md): an
    /// item row starts a list item; a content row joins the item of the nearest earlier item row of
    /// its level; deeper rows form sub-lists in the item of their parent row; a row of level 0 is a
    /// block outside the list and splits it. Blocks keep their IDs; list items and lists keep theirs
    /// where they continue, and recorded syntax and separators are dropped where they moved.
    /// Numbers follow 02's rules: a list that gains items is renumbered (unless all its numbers are
    /// the same), a list that only loses items keeps its numbers, and a new list counts from 1.
    /// </summary>
    public void WriteBack(TransactionBuilder builder)
    {
        var top = Arrange();
        var used = new HashSet<BlockId>();
        foreach (var node in top.OfType<ListNode>())
        {
            AssignIds(node, used, builder);
        }

        var blocks = top.Select(entry => entry is ListRow row ? Place(row, Parent) : Materialize((ListNode)entry, Parent, builder)).ToArray();
        var selection = builder.Selection;
        builder.RemoveBlocks(Parent, Index, 1);
        builder.InsertBlocks(Parent, Index, blocks);
        builder.Selection = new TextSelection(Restore(builder.Document, selection.Anchor), Restore(builder.Document, selection.Head));
    }

    private static void AddRows(ListBlock list, int level, BlockId parent, bool startsList, List<ListRow> rows, Dictionary<BlockId, (BlockId, int)> lists)
    {
        lists[list.Id] = (parent, level);
        for (int i = 0; i < list.Children.Count; i++)
        {
            var item = (ListItem)list.Children[i];
            if (item.Children.Count > 0 && item.Children[0] is ListBlock)
            {
                // "- - a": the item's own row is its marker alone.
                rows.Add(new ListRow
                {
                    Kind = ListRowKind.Item,
                    Level = level,
                    Item = item,
                    OriginalParent = item.Id,
                    OriginalList = list,
                    StartsList = i == 0 && startsList,
                });
            }

            for (int c = 0; c < item.Children.Count; c++)
            {
                var child = item.Children[c];
                if (child is ListBlock sublist)
                {
                    AddRows(sublist, level + 1, item.Id, startsList: c > 0 && item.Children[c - 1] is ListBlock, rows, lists);
                    continue;
                }

                rows.Add(new ListRow
                {
                    Block = child,
                    Kind = c == 0 ? ListRowKind.Item : ListRowKind.Content,
                    Level = level,
                    Item = c == 0 ? item : null,
                    OriginalParent = item.Id,
                    OriginalList = list,
                    StartsList = c == 0 && i == 0 && startsList,
                });
            }
        }
    }

    /// <summary>A list being rebuilt.</summary>
    private sealed class ListNode
    {
        public required int Level { get; init; }

        public List<ItemNode> Items { get; } = [];

        public ListBlock? Original { get; set; }

        public BlockId Id { get; set; }
    }

    /// <summary>A list item being rebuilt: its rows and sub-lists in order.</summary>
    private sealed class ItemNode
    {
        public required ListRow Row { get; init; }

        public List<object> Children { get; } = [];
    }

    /// <summary>Arranges the rows into lists and items; level-0 rows stay outside as <see cref="ListRow"/>s.</summary>
    private List<object> Arrange()
    {
        var top = new List<object>();
        var open = new List<ListNode>();
        foreach (var row in Rows)
        {
            if (row.Level <= 0)
            {
                open.Clear();
                if (!row.IsBare)
                {
                    top.Add(row);
                }

                continue;
            }

            // A row without a parent row goes as deep as it can (the behaviors never produce one).
            int level = Math.Min(row.Level, open.Count + 1);
            if (open.Count > level)
            {
                open.RemoveRange(level, open.Count - level);
            }

            if (row.Kind == ListRowKind.Content && open.Count == level)
            {
                open[level - 1].Items[^1].Children.Add(row);
                continue;
            }

            ListNode list;
            if (open.Count == level && !row.StartsList)
            {
                list = open[level - 1];
            }
            else
            {
                if (open.Count == level)
                {
                    open.RemoveAt(level - 1);
                }

                list = new ListNode { Level = level };
                if (level == 1)
                {
                    top.Add(list);
                }
                else
                {
                    open[level - 2].Items[^1].Children.Add(list);
                }

                open.Add(list);
            }

            var item = new ItemNode { Row = row };
            if (!row.IsBare)
            {
                item.Children.Add(row);
            }

            list.Items.Add(item);
        }

        // A bare row left without the rows of its sub-list is dropped with its item.
        return top.Where(static entry => entry is not ListNode list || Prune(list)).ToList();
    }

    /// <summary>Removes items left without children, and sub-lists left without items; false when the list is empty.</summary>
    private static bool Prune(ListNode list)
    {
        foreach (var item in list.Items)
        {
            item.Children.RemoveAll(static child => child is ListNode sublist && !Prune(sublist));
        }

        list.Items.RemoveAll(static item => item.Children.Count == 0);
        return list.Items.Count > 0;
    }

    /// <summary>
    /// Gives each rebuilt list the ID of an original list it continues: preferably one of the same
    /// level that its items come from, otherwise any unused one of its items' lists, otherwise a new ID.
    /// </summary>
    private void AssignIds(ListNode node, HashSet<BlockId> used, TransactionBuilder builder)
    {
        ListBlock? chosen = node.Items.Select(item => item.Row.OriginalList)
            .FirstOrDefault(list => !used.Contains(list.Id) && _lists[list.Id].Level == node.Level)
            ?? node.Items.Select(item => item.Row.OriginalList).FirstOrDefault(list => !used.Contains(list.Id));
        node.Original = chosen;
        node.Id = chosen?.Id ?? builder.NewId();
        used.Add(node.Id);
        foreach (var item in node.Items)
        {
            foreach (var sublist in item.Children.OfType<ListNode>())
            {
                AssignIds(sublist, used, builder);
            }
        }
    }

    private ListBlock Materialize(ListNode node, BlockId parent, TransactionBuilder builder)
    {
        var original = node.Original;
        var source = node.Items[0].Row.OriginalList;
        bool sameParent = original is not null && _lists[original.Id].Parent == parent;
        bool ordered = original?.IsOrdered ?? source.IsOrdered;
        var numbers = Numbers(node, ordered);
        var items = new List<Block>(node.Items.Count);
        for (int k = 0; k < node.Items.Count; k++)
        {
            var row = node.Items[k].Row;
            var originalItem = row.Item;
            var id = originalItem?.Id ?? builder.NewId();
            bool sameList = originalItem is not null && sameParent && row.OriginalList.Id == node.Id;
            var children = node.Items[k].Children
                .Select(child => child is ListRow inner ? Place(inner, id) : Materialize((ListNode)child, id, builder))
                .ToList();
            items.Add(new ListItem
            {
                Id = id,
                Children = [.. children],
                Task = row.Task,
                Number = numbers[k],
                Syntax = sameList ? originalItem!.Syntax : null,
                SeparatorBefore = sameList ? originalItem!.SeparatorBefore : null,
            });
        }

        return new ListBlock
        {
            Id = node.Id,
            IsOrdered = ordered,
            Children = [.. items],
            Syntax = original is not null ? original.Syntax : new ListSyntax(source.MarkerChar, source.Syntax?.WasLoose ?? false),
            SeparatorBefore = sameParent ? original!.SeparatorBefore : null,
        };
    }

    /// <summary>The numbers of a rebuilt list's items (02-document-model.md, Q26, Q32).</summary>
    private static int?[] Numbers(ListNode node, bool ordered)
    {
        var numbers = new int?[node.Items.Count];
        if (!ordered)
        {
            return numbers;
        }

        var original = node.Original;
        if (original is null)
        {
            for (int k = 0; k < numbers.Length; k++)
            {
                numbers[k] = k + 1;
            }

            return numbers;
        }

        bool IsAdded(ItemNode item) => item.Row.Item is null || item.Row.OriginalList.Id != original.Id;
        int start = (original.Children.Count > 0 ? ((ListItem)original.Children[0]).Number : null) ?? 1;
        bool added = node.Items.Any(IsAdded);
        bool same = added && ListRules.ClassifyNumbering(original) == ListNumbering.Same;
        for (int k = 0; k < numbers.Length; k++)
        {
            var item = node.Items[k];
            numbers[k] = !added ? item.Row.Item!.Number
                : same ? (IsAdded(item) ? start : item.Row.Item!.Number)
                : start + k;
        }

        return numbers;
    }

    /// <summary>A row's block under its new parent: the recorded separator goes when the parent changed.</summary>
    private static Block Place(ListRow row, BlockId parent) =>
        row.OriginalParent == parent || row.Block!.SeparatorBefore is null ? row.Block! : row.Block with { SeparatorBefore = null };

    private static Position Restore(Document document, Position position)
    {
        return position switch
        {
            TextPosition text when DocumentTree.Contains(document, text.Block) => text,
            BoundaryPosition boundary when DocumentTree.Contains(document, boundary.Parent) => boundary,
            _ => new BoundaryPosition(document.Id, 0),
        };
    }
}
