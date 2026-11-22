using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Block quotes (04 behavior catalog). Backspace at the start of a paragraph in a quote merges it
/// with the text leaf before it at the same quote level, or else takes it out of one level of quote;
/// Enter in an empty paragraph leaves the quote, splitting it in two when the paragraph is in the
/// middle.
/// </summary>
internal sealed class BlockQuoteBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (!state.Selection.IsCollapsed || state.Selection.Head is not TextPosition head
            || command is not (DeleteBackward or SplitBlock))
        {
            return null;
        }

        var location = DocumentTree.Locate(state.Document, head.Block);
        if (location.Parent is not BlockQuote quote || location.Block is not Paragraph paragraph)
        {
            return null;
        }

        var builder = context.Begin(state);
        switch (command)
        {
            case DeleteBackward when head.Offset == 0:
                if (location.Index > 0 && quote.Children[location.Index - 1] is TextLeafBlock previous)
                {
                    int junction = previous.Text.Length;
                    builder.InsertText(previous.Id, junction, TextBehavior.AdaptText(previous, paragraph.Text));
                    builder.RemoveBlocks(quote.Id, location.Index, 1);
                    builder.SetCaret(previous.Id, junction);
                }
                else
                {
                    LiftOut(builder, quote, location.Index);
                    builder.SetCaret(paragraph.Id, 0);
                }

                return builder.Build(TransactionKind.Edit, context.Now);

            case SplitBlock when paragraph.Text.Length == 0:
                LiftOut(builder, quote, location.Index);
                builder.SetCaret(paragraph.Id, 0);
                return builder.Build(TransactionKind.Edit, context.Now);

            default:
                return null;
        }
    }

    /// <summary>
    /// Moves child <paramref name="index"/> of the quote out into the quote's container: the children
    /// before it stay in the quote, the children after it go to a second quote. The moved block's
    /// recorded separator and quote prefixes no longer apply and are generated on export.
    /// </summary>
    private static void LiftOut(TransactionBuilder builder, BlockQuote quote, int index)
    {
        var location = builder.Locate(quote.Id);
        var before = quote.Children.GetRange(0, index);
        var after = quote.Children.GetRange(index + 1, quote.Children.Count - index - 1);
        var blocks = new List<Block>(3);
        if (!before.IsEmpty)
        {
            blocks.Add(quote with { Children = before });
        }

        blocks.Add(quote.Children[index] with { SeparatorBefore = null });
        if (!after.IsEmpty)
        {
            blocks.Add(before.IsEmpty
                ? quote with { Children = after, SeparatorBefore = null }
                : new BlockQuote { Id = builder.NewId(), Children = after });
        }

        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
        builder.InsertBlocks(location.Parent.Id, location.Index, [.. blocks]);
    }
}
