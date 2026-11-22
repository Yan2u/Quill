using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Paragraphs: Backspace at the start and Delete at the end follow the block boundary rules (Q36,
/// Q40). Enter, soft breaks and typing are generic; input rules and commits run in the session.
/// Directly inside a list item the list rules apply instead, and Backspace at the start of a
/// paragraph in a quote follows the quote rule; those behaviors come next in the chain.
/// </summary>
internal sealed class ParagraphBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not Paragraph paragraph)
        {
            return null;
        }

        var parent = DocumentTree.Locate(state.Document, paragraph.Id).Parent;
        return command switch
        {
            DeleteBackward when parent is ListItem or BlockQuote => null,
            DeleteForward when parent is ListItem => null,
            DeleteBackward when BoundaryEdits.CaretAtStart(state, paragraph.Id) => BoundaryEdits.MergeIntoPrevious(state, paragraph, context),
            DeleteForward when BoundaryEdits.CaretAtEnd(state, paragraph) => BoundaryEdits.DeleteIntoNext(state, paragraph, context),
            _ => null,
        };
    }
}

/// <summary>
/// Headings (04 behavior catalog): Enter at the start inserts an empty paragraph above and the heading
/// keeps its ID; Enter in the middle splits into two headings of the same level; Enter at the end adds
/// an empty paragraph; Shift+Enter is Enter; Backspace at the start turns the heading into a paragraph.
/// </summary>
internal sealed class HeadingBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not Heading heading)
        {
            return null;
        }

        switch (command)
        {
            case SplitBlock or InsertSoftBreak or SplitBlockAlternate:
                return Split(state, context);
            case DeleteBackward when BoundaryEdits.CaretAtStart(state, heading.Id):
            {
                var builder = context.Begin(state);
                builder.ReplaceBlock(new Paragraph
                {
                    Id = heading.Id,
                    Text = heading.Text,
                    SeparatorBefore = heading.SeparatorBefore,
                    LinePrefixes = heading.LinePrefixes,
                });
                return builder.Build(TransactionKind.Edit, context.Now);
            }

            case DeleteForward when BoundaryEdits.CaretAtEnd(state, heading):
                return DocumentTree.Locate(state.Document, heading.Id).Parent is ListItem
                    ? null
                    : BoundaryEdits.DeleteIntoNext(state, heading, context);
            default:
                return null;
        }
    }

    private static Transaction? Split(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        if (builder.Selection.Head is not TextPosition caret || builder.Find<Block>(caret.Block) is not Heading heading)
        {
            // The selection began outside the heading: what is left of it is split like a paragraph.
            if (builder.Selection.Head is TextPosition at && builder.Find<Block>(at.Block) is Paragraph)
            {
                builder.SetCaret(EditOperations.SplitLeaf(builder, at.Block, at.Offset, static (id, text) => new Paragraph { Id = id, Text = text }), 0);
            }

            return builder.Build(TransactionKind.Edit, context.Now);
        }

        if (caret.Offset == 0 && heading.Text.Length > 0)
        {
            EditOperations.InsertBefore(builder, heading.Id, EditOperations.NewParagraph(builder));
            builder.SetCaret(heading.Id, 0);
        }
        else if (caret.Offset >= heading.Text.Length)
        {
            var paragraph = EditOperations.NewParagraph(builder);
            EditOperations.InsertAfter(builder, heading.Id, paragraph);
            builder.SetCaret(paragraph.Id, 0);
        }
        else
        {
            var tail = EditOperations.SplitLeaf(builder, heading.Id, caret.Offset, (id, text) => new Heading { Id = id, Level = heading.Level, Text = text });
            builder.SetCaret(tail, 0);
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }
}

/// <summary>
/// Code, math, HTML and front matter: Enter and Shift+Enter insert a line break, Tab a tab;
/// Ctrl+Enter at the very end leaves the block into a new real paragraph (Q19). In an empty code or
/// math block, Backspace and Delete turn it into an empty paragraph (Typora deletes an empty code
/// block, checked by the user; math the same〔默认〕); otherwise Backspace at the start and Delete at the
/// end do nothing.
/// </summary>
internal sealed class RawTextBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.Find(state.Document, head.Block) is not RawTextBlock block)
        {
            return null;
        }

        // Code lives in a container with its language (Q91): structural edits act on the container.
        var outer = (Block?)CodeFence.Of(state.Document, block) ?? block;

        switch (command)
        {
            case SplitBlock or InsertSoftBreak:
                return TextBehavior.InsertText(state, "\n", context, TransactionKind.Edit);
            case Indent { InsertsTab: true }:
                return TextBehavior.InsertText(state, "\t", context);
            case Indent:
                return NoChange.For(state);
            case SplitBlockAlternate:
            {
                var builder = context.Begin(state);
                if (BoundaryEdits.CaretAtEnd(state, block))
                {
                    var paragraph = EditOperations.NewParagraph(builder);
                    EditOperations.InsertAfter(builder, outer.Id, paragraph);
                    builder.SetCaret(paragraph.Id, 0);
                }

                return builder.Build(TransactionKind.Edit, context.Now) ?? NoChange.For(state);
            }

            case DeleteBackward or DeleteForward when block is CodeBlock or MathBlock && block.Text.Length == 0 && state.Selection.IsCollapsed:
            {
                var builder = context.Begin(state);
                var paragraph = EditOperations.NewParagraph(builder);
                var location = builder.Locate(outer.Id);
                builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
                builder.InsertBlocks(location.Parent.Id, location.Index, paragraph with { SeparatorBefore = outer.SeparatorBefore });
                builder.SetCaret(paragraph.Id, 0);
                return builder.Build(TransactionKind.Edit, context.Now);
            }

            case DeleteBackward when BoundaryEdits.CaretAtStart(state, block.Id):
            case DeleteForward when BoundaryEdits.CaretAtEnd(state, block):
            case Outdent:
                return NoChange.For(state);
            default:
                return null;
        }
    }
}

/// <summary>A handled command that changes nothing (stops the dispatch chain).</summary>
internal static class NoChange
{
    public static Transaction For(EditorState state) => new()
    {
        Steps = [],
        InverseSteps = [],
        SelectionBefore = state.Selection,
        SelectionAfter = state.Selection,
        Before = state.Document,
        Result = state.Document,
    };
}
