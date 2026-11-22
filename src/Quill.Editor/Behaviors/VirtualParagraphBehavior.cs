using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// A caret in a virtual paragraph (02-document-model.md, Q19). Typing, pasting and Enter go to the
/// generic behaviors, which first make the paragraph real. Backspace moves into the block before it
/// the way an empty paragraph merges (to its last text leaf, Q40), or selects it whole when it has no
/// text; Delete selects the block after it whole (Q36).
/// </summary>
internal sealed class VirtualParagraphBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (!state.Selection.IsCollapsed || state.Selection.Head is not BoundaryPosition boundary
            || command is not (DeleteBackward or DeleteForward))
        {
            return null;
        }

        var container = DocumentTree.Find<ContainerBlock>(state.Document, boundary.Parent);
        var builder = context.Begin(state);
        if (command is DeleteBackward)
        {
            if (boundary.Index == 0)
            {
                return NoChange.For(state);
            }

            var target = BoundaryEdits.MergeTarget(container.Children[boundary.Index - 1]);
            builder.Selection = target is not null
                ? TextSelection.Caret(target.Id, target.Text.Length)
                : BoundaryEdits.WholeBlock(container, boundary.Index - 1);
        }
        else
        {
            if (boundary.Index >= container.Children.Count)
            {
                return NoChange.For(state);
            }

            builder.Selection = BoundaryEdits.WholeBlock(container, boundary.Index);
        }

        return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
    }
}
