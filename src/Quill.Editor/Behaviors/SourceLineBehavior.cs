using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Editing a line of a source document (11-source-mode.md): Enter splits the line (the new line keeps
/// the leading whitespace〔默认〕), Backspace at the start and Delete at the end join two lines, text with
/// line breaks (pasting, typing) becomes several lines, Tab inserts a tab and Shift+Tab removes one
/// level of indentation. No input rules or commits; formatting and block commands do nothing〔默认〕.
/// </summary>
internal sealed class SourceLineBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition head || DocumentTree.TryFind(state.Document, head.Block) is not SourceLine line)
        {
            return null;
        }

        bool collapsed = state.Selection.IsCollapsed;
        switch (command)
        {
            case InsertText insert when insert.Text.Contains('\n') || insert.Text.Contains('\r'):
                return InsertLines(state, insert.Text, context);
            case Paste paste:
                return InsertLines(state, paste.Content.Markdown ?? paste.Content.PlainText, context);
            case PasteAsPlainText paste:
                return InsertLines(state, paste.Text, context);
            case SplitBlock or SplitBlockAlternate or InsertSoftBreak:
                return Split(state, context);
            case DeleteBackward when collapsed && head.Offset == 0:
                return Navigation.AdjacentLeaf(state.Document, line.Id, forward: false) is SourceLine previous
                    ? Join(state, previous, line, context)
                    : NoChange.For(state);
            case DeleteForward when collapsed && head.Offset == line.Text.Length:
                return Navigation.AdjacentLeaf(state.Document, line.Id, forward: true) is SourceLine next
                    ? Join(state, line, next, context)
                    : NoChange.For(state);
            case Outdent:
                return RemoveIndent(state, line, head, context);
            case InsertText or DeleteBackward or DeleteForward or Cut or DeleteWord or ReplaceRange or Indent { InsertsTab: true }:
                // The generic text behaviors: one line, raw text.
                return null;
            default:
                return NoChange.For(state);
        }
    }

    /// <summary>Joins <paramref name="second"/> to the end of <paramref name="first"/>; the caret at the join.</summary>
    private static Transaction? Join(EditorState state, SourceLine first, SourceLine second, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.MergeLeaves(builder, first.Id, second.Id);
        builder.SetCaret(first.Id, first.Text.Length);
        return builder.Build(TransactionKind.Deletion, context.Now, first.Id);
    }

    private static Transaction? Split(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        var caret = (TextPosition)builder.Selection.Head;
        string text = builder.Find<LeafBlock>(caret.Block).Text;
        string indent = text[..LeadingWhitespace(text, caret.Offset)];
        var tail = EditOperations.SplitLeaf(builder, caret.Block, caret.Offset, (id, rest) => new SourceLine { Id = id, Text = indent + rest });
        builder.SetCaret(tail, indent.Length);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Text with line breaks: the first line joins the text before the caret, the last the text after it.</summary>
    private static Transaction? InsertLines(EditorState state, string text, BehaviorContext context)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        var caret = (TextPosition)builder.Selection.Head;
        if (lines.Length == 1)
        {
            builder.InsertText(caret.Block, caret.Offset, lines[0]);
            builder.SetCaret(caret.Block, caret.Offset + lines[0].Length);
            return builder.Build(TransactionKind.Edit, context.Now, caret.Block);
        }

        string current = builder.Find<LeafBlock>(caret.Block).Text;
        string after = current[caret.Offset..];
        builder.ReplaceText(caret.Block, caret.Offset, current.Length, lines[0]);
        var location = builder.Locate(caret.Block);
        var inserted = lines.Skip(1).Select((line, i) => (Block)new SourceLine
        {
            Id = builder.NewId(),
            Text = i == lines.Length - 2 ? line + after : line,
        }).ToArray();
        builder.InsertBlocks(location.Parent!.Id, location.Index + 1, inserted);
        builder.SetCaret(inserted[^1].Id, lines[^1].Length);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Shift+Tab: removes a leading tab, or up to four leading spaces〔默认〕.</summary>
    private static Transaction? RemoveIndent(EditorState state, SourceLine line, TextPosition head, BehaviorContext context)
    {
        int count = line.Text.StartsWith('\t') ? 1 : line.Text.TakeWhile(static c => c == ' ').Take(4).Count();
        if (count == 0)
        {
            return NoChange.For(state);
        }

        var builder = context.Begin(state);
        builder.ReplaceText(line.Id, 0, count, string.Empty);
        builder.SetCaret(line.Id, Math.Max(0, head.Offset - count));
        return builder.Build(TransactionKind.Edit, context.Now, line.Id);
    }

    /// <summary>The length of the whitespace at the start of a line, up to <paramref name="limit"/>.</summary>
    private static int LeadingWhitespace(string text, int limit)
    {
        int length = 0;
        while (length < limit && length < text.Length && text[length] is ' ' or '\t')
        {
            length++;
        }

        return length;
    }
}
