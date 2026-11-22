using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// The language of a code block (Q91): one line of plain text. Enter, Shift+Enter and Ctrl+Enter go back
/// to the end of the code; Backspace at the start and Delete at the end do nothing (no merging with the
/// code); Tab does nothing. Pasted line breaks are dropped (<see cref="TextBehavior.AdaptText"/>).
/// </summary>
internal sealed class CodeLanguageBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not CodeLanguage language
            || CodeFence.Of(state.Document, language) is not { } fence)
        {
            return null;
        }

        bool collapsed = state.Selection.IsCollapsed;
        switch (command)
        {
            case SplitBlock or InsertSoftBreak or SplitBlockAlternate:
            {
                var builder = context.Begin(state);
                builder.SetCaret(fence.Code.Id, fence.Code.Text.Length);
                return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
            }

            case DeleteBackward when collapsed && head.Offset == 0:
            case DeleteForward when collapsed && head.Offset == language.Text.Length:
            case Indent or Outdent:
                return NoChange.For(state);
            default:
                return null;
        }
    }
}
