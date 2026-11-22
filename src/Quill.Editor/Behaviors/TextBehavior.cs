using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.State;
using Quill.Editor.Text;
using Quill.Editor.Transactions;

namespace Quill.Editor.Behaviors;

/// <summary>
/// Generic text editing, last in the dispatch chain (04-commands-and-behaviors.md): typing, deleting
/// inside a leaf, soft breaks, splitting, and Tab as a tab character. Element behaviors run before it
/// and handle everything element-specific, block boundaries included.
/// </summary>
internal sealed class TextBehavior : IBehavior
{
    public Transaction? TryHandle(EditorState state, Command command, BehaviorContext context)
    {
        return command switch
        {
            InsertText insert => InsertText(state, insert.Text, context),
            DeleteBackward delete => Delete(state, delete.Unit, forward: false, context),
            DeleteForward delete => Delete(state, delete.Unit, forward: true, context),
            InsertSoftBreak => InsertText(state, "\n", context, TransactionKind.Edit),
            SplitBlock or SplitBlockAlternate => Split(state, context),
            Indent { InsertsTab: true } => InsertText(state, "\t", context),
            Cut => DeleteSelection(state, context),
            SetHeadingLevel level => SetHeadingLevel(state, level.Level, context),
            ToggleTaskItem toggle => ToggleTask(state, toggle.Item, context),
            SetCodeLanguage language => SetLanguage(state, language, context),
            InsertTable table => InsertTableAtCaret(state, table, context),
            ToggleInline toggle => InlineFormatting.Toggle(state, toggle.Format, context),
            InsertLink link => InlineFormatting.Link(state, link.Url, image: false, context),
            InsertImage image => InlineFormatting.Link(state, image.Url, image: true, context),
            ClearFormat => InlineFormatting.Clear(state, context),
            ChangeHeadingLevel change when state.Selection.Head is TextPosition head && DocumentTree.TryFind(state.Document, head.Block) is { } leaf
                && BlockFormatting.HeadingLevel(leaf, change.Delta) is { } level => SetHeadingLevel(state, level, context),
            ToggleBlock { Format: BlockFormat.Quote } => BlockFormatting.ToggleQuote(state, context),
            ToggleBlock block => BlockFormatting.ToggleRaw(state, block.Format == BlockFormat.MathBlock, context),
            ToggleList list => BlockFormatting.ToggleList(state, list.Kind, context),
            InsertBlock insert => BlockFormatting.Insert(state, insert.Block, context),
            DeleteWord => DeleteWordAtCaret(state, context),
            Paste paste => Clipboard.Paste(state, paste.Content.Markdown ?? paste.Content.PlainText, context),
            PasteAsPlainText paste => Clipboard.Paste(state, paste.Text, context),
            _ => null,
        };
    }

    /// <summary>Replaces the selection with text; a caret in a virtual paragraph first gets a real paragraph.</summary>
    public static Transaction? InsertText(EditorState state, string text, BehaviorContext context, TransactionKind kind = TransactionKind.Typing)
    {
        var builder = context.Begin(state);
        if (EditOperations.DeleteSelection(builder))
        {
            kind = TransactionKind.Edit;
        }

        var caret = EnsureTextCaret(builder);
        var leaf = builder.Find<LeafBlock>(caret.Block);
        text = AdaptText(leaf, text);
        builder.InsertText(caret.Block, caret.Offset, text);
        builder.SetCaret(caret.Block, caret.Offset + text.Length);
        if (text.Contains('\n'))
        {
            kind = TransactionKind.Edit;
        }

        return builder.Build(kind, context.Now, caret.Block);
    }

    /// <summary>Turns a caret in a virtual paragraph into a caret in a new real paragraph (02, Q19).</summary>
    public static TextPosition EnsureTextCaret(TransactionBuilder builder)
    {
        if (builder.Selection.Head is TextPosition text)
        {
            return text;
        }

        var boundary = (BoundaryPosition)builder.Selection.Head;
        var paragraph = EditOperations.NewParagraph(builder);
        builder.InsertBlocks(boundary.Parent, boundary.Index, paragraph);
        builder.SetCaret(paragraph.Id, 0);
        return new TextPosition(paragraph.Id, 0);
    }

    /// <summary>
    /// What a leaf can hold: a table cell stores '|' as "\|" and a line break as &lt;br&gt; (Q37, Q40);
    /// headings and definition slots hold a single line.
    /// </summary>
    public static string AdaptText(LeafBlock leaf, string text)
    {
        return leaf switch
        {
            TableCell => EscapeCellPipes(text).Replace("\n", "<br>"),
            Heading or DefinitionSlot => text.Replace('\n', ' '),

            // The language of a code block is one line: line breaks are dropped (Q91).
            CodeLanguage => text.Replace("\r", string.Empty).Replace("\n", string.Empty),
            _ => text,
        };
    }

    // TODO(Q40): '|' typed into a cell is stored as "\|" (the default answer of Q40). The normalizer
    // also escapes pipes that an edit leaves unescaped next to the inserted text.
    private static string EscapeCellPipes(string text)
    {
        var builder = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '|' && !Normalizer.IsEscaped(text, i))
            {
                builder.Append('\\');
            }

            builder.Append(text[i]);
        }

        return builder.ToString();
    }

    private static Transaction? Delete(EditorState state, TextUnit unit, bool forward, BehaviorContext context)
    {
        if (!state.Selection.IsCollapsed)
        {
            return DeleteSelection(state, context);
        }

        if (state.Selection.Head is not TextPosition caret)
        {
            return null;
        }

        var leaf = DocumentTree.Find<LeafBlock>(state.Document, caret.Block);
        string text = leaf.Text;
        int from = caret.Offset;
        int to = caret.Offset;
        if (forward)
        {
            if (to >= text.Length)
            {
                return null;
            }

            to = unit == TextUnit.Word ? TextBoundaries.NextWord(text, to) : TextBoundaries.NextGrapheme(text, to);

            // The leading zero-width space goes together with the whitespace after it (05).
            if (from == 0 && text[0] == Normalizer.ZeroWidthSpace && to == 1)
            {
                to = TextBoundaries.NextGrapheme(text, 1);
            }
        }
        else
        {
            if (from == 0)
            {
                return null;
            }

            from = unit == TextUnit.Word ? TextBoundaries.PreviousWord(text, from) : TextBoundaries.PreviousGrapheme(text, from);
        }

        var builder = context.Begin(state);
        builder.ReplaceText(caret.Block, from, to, string.Empty);
        builder.SetCaret(caret.Block, from);
        return builder.Build(TransactionKind.Deletion, context.Now, caret.Block);
    }

    /// <summary>
    /// Ctrl+T (Typora, checked by the user): an empty paragraph becomes the table; in a paragraph with
    /// text the paragraph is split at the caret and the table goes between the two halves (no empty
    /// half is left). After a heading the table follows it〔默认〕; in cells, code and other raw text
    /// nothing happens〔默认〕. The caret goes to the first header cell.
    /// </summary>
    private static Transaction? InsertTableAtCaret(EditorState state, InsertTable command, BehaviorContext context)
    {
        if (command.Columns < 1 || command.Rows < 1)
        {
            return null;
        }

        var builder = context.Begin(state);
        var table = TableEdits.NewTable(builder, command.Columns, command.Rows);
        if (!BlockFormatting.PlaceAtCaret(builder, table))
        {
            return null;
        }

        builder.SetCaret(((ContainerBlock)table.Children[0]).Children[0].Id, 0);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Ctrl+Shift+D: the selection, or the whole word at the caret.</summary>
    private static Transaction? DeleteWordAtCaret(EditorState state, BehaviorContext context)
    {
        if (!state.Selection.IsCollapsed)
        {
            return DeleteSelection(state, context);
        }

        if (state.Selection.Head is not TextPosition caret || DocumentTree.TryFind(state.Document, caret.Block) is not LeafBlock leaf)
        {
            return null;
        }

        var (start, end) = TextBoundaries.WordAt(leaf.Text, caret.Offset);
        if (end <= start)
        {
            return null;
        }

        var builder = context.Begin(state);
        builder.ReplaceText(leaf.Id, start, end, string.Empty);
        builder.SetCaret(leaf.Id, start);
        return builder.Build(TransactionKind.Deletion, context.Now, leaf.Id);
    }

    public static Transaction? DeleteSelection(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        return EditOperations.DeleteSelection(builder) ? builder.Build(TransactionKind.Edit, context.Now) : null;
    }

    /// <summary>Splits a paragraph at the caret; the text after it becomes a new paragraph.</summary>
    private static Transaction? Split(EditorState state, BehaviorContext context)
    {
        var builder = context.Begin(state);
        EditOperations.DeleteSelection(builder);
        var caret = EnsureTextCaret(builder);
        if (builder.Find<LeafBlock>(caret.Block) is not Paragraph)
        {
            return null;
        }

        var tail = EditOperations.SplitLeaf(builder, caret.Block, caret.Offset, static (id, text) => new Paragraph { Id = id, Text = text });
        builder.SetCaret(tail, 0);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    private static Transaction? SetHeadingLevel(EditorState state, int level, BehaviorContext context)
    {
        if (state.Selection.Head is not TextPosition caret)
        {
            return null;
        }

        var leaf = DocumentTree.Find(state.Document, caret.Block);
        Block? converted = (leaf, level) switch
        {
            (Paragraph paragraph, > 0) => new Heading
            {
                Id = paragraph.Id,
                Level = level,
                Text = paragraph.Text,
                SeparatorBefore = paragraph.SeparatorBefore,
                LinePrefixes = paragraph.LinePrefixes,
            },
            (Heading heading, > 0) when heading.Level != level => heading with { Level = level },
            (Heading heading, 0) => new Paragraph
            {
                Id = heading.Id,
                Text = heading.Text,
                SeparatorBefore = heading.SeparatorBefore,
                LinePrefixes = heading.LinePrefixes,
            },
            _ => null,
        };

        if (converted is null)
        {
            return null;
        }

        var builder = context.Begin(state);
        builder.ReplaceBlock(converted);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    private static Transaction? ToggleTask(EditorState state, BlockId itemId, BehaviorContext context)
    {
        if (!DocumentTree.Contains(state.Document, itemId) || DocumentTree.Find(state.Document, itemId) is not ListItem { Task: not TaskState.None } item)
        {
            return null;
        }

        var builder = context.Begin(state);
        builder.ReplaceBlock(item with { Task = item.Task == TaskState.Checked ? TaskState.Unchecked : TaskState.Checked });
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    private static Transaction? SetLanguage(EditorState state, SetCodeLanguage command, BehaviorContext context)
    {
        if (!DocumentTree.Contains(state.Document, command.Block) || DocumentTree.Find(state.Document, command.Block) is not { } block
            || (block as CodeFence ?? CodeFence.Of(state.Document, block)) is not { } fence)
        {
            return null;
        }

        var builder = context.Begin(state);
        builder.ReplaceText(fence.Language.Id, 0, fence.Language.Text.Length, AdaptText(fence.Language, command.Info.Trim()));
        return builder.Build(TransactionKind.Edit, context.Now);
    }
}
