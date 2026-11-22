using Quill.Editor.Behaviors;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Backspace at the start and Delete at the end of a paragraph or heading (04, Q36, Q40).
/// </summary>
internal static class BoundaryEdits
{
    /// <summary>
    /// Backspace at the start of a text block that has a previous sibling: merge into the last text
    /// leaf of that sibling (the last cell of a table, the content of a footnote, the title of a link
    /// definition, the last leaf of a quote or list, the end of a code or math block, Q40), or select
    /// the sibling whole when it holds no text (a rule, a TOC). Null when there is no previous sibling.
    /// </summary>
    public static Transaction? MergeIntoPrevious(EditorState state, TextLeafBlock block, BehaviorContext context)
    {
        var location = DocumentTree.Locate(state.Document, block.Id);
        if (location.Index <= 0)
        {
            return null;
        }

        var previous = location.Parent!.Children[location.Index - 1];
        var builder = context.Begin(state);
        var target = MergeTarget(previous);
        if (target is null)
        {
            builder.Selection = WholeBlock(location.Parent, location.Index - 1);
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        int junction = target.Text.Length;
        builder.InsertText(target.Id, junction, TextBehavior.AdaptText(target, block.Text));
        builder.RemoveBlocks(location.Parent.Id, location.Index, 1);
        builder.SetCaret(target.Id, junction);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Delete at the end of a text block: a following paragraph or heading merges up; a following
    /// block without text or a container is selected whole first (Q36). At the end of a quote the
    /// block after the quote counts. Null when nothing follows.
    /// </summary>
    public static Transaction? DeleteIntoNext(EditorState state, LeafBlock block, BehaviorContext context)
    {
        var location = DocumentTree.Locate(state.Document, block.Id);
        int level = location.Ancestors.Length - 1;
        while (level > 0 && location.Ancestors[level].Index + 1 >= location.Ancestors[level].Container.Children.Count
            && location.Ancestors[level].Container is BlockQuote)
        {
            level--;
        }

        var (parent, index) = location.Ancestors[level];
        if (index + 1 >= parent.Children.Count)
        {
            return null;
        }

        var next = parent.Children[index + 1];
        var builder = context.Begin(state);
        if (next is Paragraph or Heading)
        {
            int junction = block.Text.Length;
            EditOperations.MergeLeaves(builder, block.Id, next.Id);
            builder.SetCaret(block.Id, junction);
        }
        else
        {
            builder.Selection = WholeBlock(parent, index + 1);
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// The leaf that a following text block merges into (Q40): the title of a link definition, the
    /// content of a footnote, otherwise the last leaf; null for blocks without text.
    /// </summary>
    public static LeafBlock? MergeTarget(Block previous) => previous switch
    {
        LinkReferenceDefinition definition => definition.Title,
        FootnoteDefinition { Children.Count: 2 } footnote => footnote.Content,
        CodeFence code => code.Code,
        ThematicBreak or TocBlock => null,
        _ => Navigation.LastLeaf(previous),
    };

    /// <summary>
    /// Selects one child of a container whole (Q36): from the boundary before it to the boundary after
    /// it. A block that takes a block caret (a TOC or a rule) gets one instead (Q59, Q60).
    /// </summary>
    public static TextSelection WholeBlock(ContainerBlock parent, int index) =>
        BlockRules.TakesBlockCaret(parent.Children[index])
            ? TextSelection.BlockCaret(parent.Id, index)
            : new(new BoundaryPosition(parent.Id, index), new BoundaryPosition(parent.Id, index + 1));

    /// <summary>Whether the selection is a caret at the start of the block.</summary>
    public static bool CaretAtStart(EditorState state, BlockId block) =>
        state.Selection.IsCollapsed && state.Selection.Head is TextPosition { Offset: 0 } head && head.Block == block;

    /// <summary>Whether the selection is a caret at the end of the leaf.</summary>
    public static bool CaretAtEnd(EditorState state, LeafBlock leaf) =>
        state.Selection.IsCollapsed && state.Selection.Head is TextPosition head && head.Block == leaf.Id && head.Offset == leaf.Text.Length;
}
