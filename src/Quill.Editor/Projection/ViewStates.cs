using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Projection;

/// <summary>
/// Computes the blocks whose view state differs from the default: the focused leaf and the containers
/// it activates, the blocks the selection touches, the composition and the virtual paragraph. Work is
/// proportional to the selection, plus the siblings scanned to find it.
/// </summary>
internal static class ViewStates
{
    public static Dictionary<BlockId, BlockViewState> Compute(EditorState state, ActiveSet active)
    {
        var document = state.Document;
        var states = new Dictionary<BlockId, BlockViewState>();
        if (active.FocusedBlock is { } focused)
        {
            Update(states, focused, s => s with { IsFocused = true, ActiveKey = Activation.KeyOf(active.ActiveNodes) });
            foreach (var container in DocumentTree.Locate(document, focused).ContainersInnermostFirst())
            {
                if (container is Table or FootnoteDefinition or LinkReferenceDefinition or CodeFence)
                {
                    Update(states, container.Id, static s => s with { IsActive = true });
                }
            }
        }

        if (state.Composition is { } composition && DocumentTree.Contains(document, composition.Block))
        {
            Update(states, composition.Block, s => s with { CompositionStart = composition.Start, CompositionEnd = composition.Start + composition.Length });
        }

        var selection = state.Selection;
        if (selection.IsBlockCaret)
        {
            if (active.FocusedBlock is { } block)
            {
                Update(states, block, static s => s with { HasBlockCaret = true });
            }
        }
        else if (selection.IsCollapsed)
        {
            if (selection.Head is BoundaryPosition boundary && DocumentTree.Contains(document, boundary.Parent))
            {
                Update(states, boundary.Parent, s => s with { GapIndex = boundary.Index });
            }
        }
        else
        {
            var (start, end) = PositionOrder.Ordered(document, selection);
            MarkSelection(document, document, start, end, states);
        }

        return states;
    }

    private static void MarkSelection(Document document, ContainerBlock container, Position start, Position end, Dictionary<BlockId, BlockViewState> states)
    {
        for (int i = 0; i < container.Children.Count; i++)
        {
            var before = new BoundaryPosition(container.Id, i);
            var after = new BoundaryPosition(container.Id, i + 1);
            if (PositionOrder.Compare(document, after, start) <= 0)
            {
                continue;
            }

            if (PositionOrder.Compare(document, before, end) >= 0)
            {
                break;
            }

            var child = container.Children[i];
            if (PositionOrder.Compare(document, start, before) <= 0 && PositionOrder.Compare(document, after, end) <= 0)
            {
                MarkWhole(child, states);
            }
            else if (child is LeafBlock leaf)
            {
                int from = start is TextPosition s && s.Block == leaf.Id ? s.Offset : 0;
                int to = end is TextPosition e && e.Block == leaf.Id ? e.Offset : leaf.Text.Length;
                if (to > from)
                {
                    Update(states, leaf.Id, st => st with { SelectionStart = from, SelectionEnd = to });
                }
            }
            else if (child is ContainerBlock inner)
            {
                MarkSelection(document, inner, start, end, states);
            }
        }
    }

    private static void MarkWhole(Block block, Dictionary<BlockId, BlockViewState> states)
    {
        int length = block is LeafBlock leaf ? leaf.Text.Length : 0;
        Update(states, block.Id, s => s with { IsSelected = true, SelectionStart = length > 0 ? 0 : -1, SelectionEnd = length > 0 ? length : -1 });
        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                MarkWhole(child, states);
            }
        }
    }

    private static void Update(Dictionary<BlockId, BlockViewState> states, BlockId id, Func<BlockViewState, BlockViewState> change) =>
        states[id] = change(states.GetValueOrDefault(id, BlockViewState.None));
}
