using Quill.Editor.Documents;

namespace Quill.Editor.Editing.Lists;

/// <summary>
/// The kind of a row in the list row view (02-document-model.md).
/// </summary>
internal enum ListRowKind
{
    /// <summary>The first child of a list item, shown with the bullet or number.</summary>
    Item,

    /// <summary>A later child of a list item: a headless list item.</summary>
    Content,
}

/// <summary>
/// One row of the list row view: a block of a list item other than a sub-list, with its kind and
/// level (0 for a block outside the list, 1 for the top-level list). The other properties remember
/// where the row came from, so that write-back can keep IDs and recorded syntax. An item whose first
/// child is a sub-list (<c>- - a</c>, Q48) has a bare row: its marker, without a block.
/// </summary>
internal sealed record ListRow
{
    /// <summary>The block; null for a bare item row.</summary>
    public Block? Block { get; init; }

    public bool IsBare => Block is null;

    public required ListRowKind Kind { get; init; }

    public required int Level { get; init; }

    /// <summary>For item rows: the list item the row opened, or null for an item made by the edit.</summary>
    public ListItem? Item { get; init; }

    /// <summary>The task state of a new item row (rows with an <see cref="Item"/> use its state).</summary>
    public TaskState NewTask { get; init; }

    /// <summary>The list item the block was a child of; null for a block made by the edit.</summary>
    public BlockId? OriginalParent { get; init; }

    /// <summary>The list the row's item belonged to: the type and marker of new lists come from it.</summary>
    public required ListBlock OriginalList { get; init; }

    /// <summary>
    /// For the first item row of a list that directly follows another list in the same container:
    /// write-back starts a new list here even after rows of the same level, keeping the lists apart.
    /// </summary>
    public bool StartsList { get; init; }

    public TaskState Task => Item?.Task ?? NewTask;
}
