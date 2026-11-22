using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Copy and cut without a selection (04, Q110, Q116): with <see cref="EditorOptions.CopyLineWithoutSelection"/>
/// on, a caret copies the text block it is in (a paragraph, heading, table cell…) or, in a code or math
/// block, its line. Cutting removes the block, or the line with its line break; a table cell, a
/// definition's slot or a code language is only cleared. The copied text pastes like any other.
/// </summary>
internal static class LineClipboard
{
    /// <summary>The block or line at the caret as clipboard content; null when there is none.</summary>
    public static ClipboardContent? Copy(EditorState state, EditorOptions options, bool displayed)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.TryFind(state.Document, head.Block) is not LeafBlock leaf)
        {
            return null;
        }

        if (SourceText.IsSource(state.Document))
        {
            return new ClipboardContent(leaf.Text, leaf.Text);
        }

        if (leaf is RawTextBlock)
        {
            var (start, end) = LineAt(leaf.Text, head.Offset);
            string line = leaf.Text[start..end];
            return new ClipboardContent(line, line);
        }

        Block block = leaf is Paragraph or Heading ? leaf with { SeparatorBefore = null } : new Paragraph { Id = leaf.Id, Text = leaf.Text };
        string markdown = MarkdownSerializer.SerializeFragment([block], options);
        string plain = !displayed && options.CopyMarkdownSource ? markdown : Clipboard.PlainText([block], ReferenceIndex.Of(state.Document), options);
        return new ClipboardContent(markdown, plain);
    }

    /// <summary>Removes the block or line at the caret; null when there is nothing to cut.</summary>
    public static Transaction? Cut(EditorState state, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.TryFind(state.Document, head.Block) is not LeafBlock leaf)
        {
            return null;
        }

        var builder = context.Begin(state);
        if (leaf is RawTextBlock && !SourceText.IsSource(state.Document))
        {
            CutLine(builder, leaf, head.Offset);
        }
        else if (leaf is TableCell or DefinitionSlot or CodeLanguage || !RemoveBlock(builder, state.Document, leaf))
        {
            builder.ReplaceText(leaf.Id, 0, leaf.Text.Length, string.Empty);
            builder.SetCaret(leaf.Id, 0);
        }

        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>The line around <paramref name="offset"/>: from after the line break before it to the next one.</summary>
    public static (int Start, int End) LineAt(string text, int offset)
    {
        int start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int end = text.IndexOf('\n', offset);
        return (start, end < 0 ? text.Length : end);
    }

    /// <summary>Removes a line and its line break (the one before it, for the last line); the caret goes to the start of the line that takes its place.</summary>
    private static void CutLine(TransactionBuilder builder, LeafBlock leaf, int offset)
    {
        var (start, end) = LineAt(leaf.Text, offset);
        if (end < leaf.Text.Length)
        {
            builder.ReplaceText(leaf.Id, start, end + 1, string.Empty);
            builder.SetCaret(leaf.Id, start);
        }
        else if (start > 0)
        {
            builder.ReplaceText(leaf.Id, start - 1, end, string.Empty);
            builder.SetCaret(leaf.Id, LineAt(leaf.Text, start - 1).Start);
        }
        else
        {
            builder.ReplaceText(leaf.Id, 0, end, string.Empty);
            builder.SetCaret(leaf.Id, 0);
        }
    }

    /// <summary>
    /// Removes a text block; containers it leaves empty go too (the normalizer). The caret goes to the start
    /// of the next leaf, or the end of the previous one. False when the block cannot go: it is the only
    /// leaf, or the head of a list item with more blocks (its item would lose its head).
    /// </summary>
    private static bool RemoveBlock(TransactionBuilder builder, Document document, LeafBlock leaf)
    {
        var location = DocumentTree.Locate(document, leaf.Id);
        if (location.Parent is not { } parent || (parent is ListItem && location.Index == 0 && parent.Children.Count > 1))
        {
            return false;
        }

        var next = Navigation.AdjacentLeaf(document, leaf.Id, forward: true);
        var previous = next is null ? Navigation.AdjacentLeaf(document, leaf.Id, forward: false) : null;
        if (next is null && previous is null)
        {
            return false;
        }

        builder.RemoveBlocks(parent.Id, location.Index, 1);
        builder.SetCaret(next is not null ? new TextPosition(next.Id, 0) : new TextPosition(previous!.Id, previous.Text.Length));
        return true;
    }
}
