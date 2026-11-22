using Quill.Editor.Commands;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// The caret standing on a whole block, a TOC or a thematic break (Q59, Q60, Typora): the block is
/// read-only. Backspace, Delete and Cut delete it, as for a selection of the block; every other edit
/// does nothing. First in the dispatch chain.
/// </summary>
internal sealed class BlockCaretBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (!state.Selection.IsBlockCaret)
        {
            return null;
        }

        return command switch
        {
            // The generic behaviors delete the covered block.
            DeleteBackward or DeleteForward or Cut => null,

            // Element interactions name their own target.
            ToggleTaskItem or SetCodeLanguage => null,
            _ => NoChange.For(state),
        };
    }
}
