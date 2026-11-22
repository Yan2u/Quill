using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Commands;

/// <summary>
/// A platform-independent editing intent (04-commands-and-behaviors.md). Commands never carry
/// platform types such as virtual keys or XAML objects.
/// </summary>
public abstract record Command;

/// <summary>Units for deleting and moving.</summary>
public enum TextUnit
{
    Grapheme,
    Word,
    VisualLine,
    LineBoundary,
    Paragraph,
    Page,
    Document,
}

public enum MoveDirection
{
    Backward,
    Forward,
    Up,
    Down,
}

/// <summary>Types text at the selection, replacing it.</summary>
public sealed record InsertText(string Text) : Command;

/// <summary>Backspace.</summary>
public sealed record DeleteBackward(TextUnit Unit = TextUnit.Grapheme) : Command;

/// <summary>Delete.</summary>
public sealed record DeleteForward(TextUnit Unit = TextUnit.Grapheme) : Command;

/// <summary>Shift+Enter.</summary>
public sealed record InsertSoftBreak : Command;

/// <summary>Enter.</summary>
public sealed record SplitBlock : Command;

/// <summary>Ctrl+Enter.</summary>
public sealed record SplitBlockAlternate : Command;

/// <summary>Tab.</summary>
/// <param name="InsertsTab">
/// Tab inserts a tab character where nothing else applies; Ctrl+[ (Typora's "Indent") only indents
/// list items and moves between table cells.
/// </param>
public sealed record Indent(bool InsertsTab = true) : Command;

/// <summary>Shift+Tab.</summary>
public sealed record Outdent : Command;

/// <summary>Moves the head, collapsing the selection unless <see cref="Extend"/>.</summary>
public sealed record MoveCaret(MoveDirection Direction, TextUnit Unit, bool Extend = false) : Command;

/// <param name="Exact">
/// The selection as given, without the markers following it (03, Q18): find jumps to a match exactly
/// (12-find-replace.md).
/// </param>
public sealed record SetSelection(TextSelection Selection, bool Exact = false) : Command;

public sealed record SelectAll : Command;

/// <summary>Double-click: selects the word (or run of punctuation or spaces) at a position (07).</summary>
public sealed record SelectWord(Position At) : Command;

/// <summary>Triple-click: selects the whole text block holding a position (07).</summary>
public sealed record SelectBlock(Position At) : Command;

/// <summary>
/// Ctrl+T: inserts an empty table of <paramref name="Columns"/> × <paramref name="Rows"/> (the header row
/// included) at the caret; the view asks for the size first.
/// </summary>
public sealed record InsertTable(int Columns, int Rows) : Command;

/// <summary>The inline formats of the Format menu (M6.0).</summary>
public enum InlineFormat
{
    Strong,
    Emphasis,
    Underline,
    Code,
    Strikethrough,
    Math,
    Superscript,
    Subscript,
    Comment,
}

/// <summary>Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+Shift+`, Alt+Shift+5 and the other Format menu entries: toggles a format on the selection.</summary>
public sealed record ToggleInline(InlineFormat Format) : Command;

/// <summary>Ctrl+K: makes the selection a link to <paramref name="Url"/> (the view passes a URL found on the clipboard), or removes a link.</summary>
public sealed record InsertLink(string? Url = null) : Command;

/// <summary>Ctrl+Shift+I: an image with the selection as its alt text.</summary>
public sealed record InsertImage(string? Url = null) : Command;

/// <summary>Ctrl+\: removes the inline formats of the selection.</summary>
public sealed record ClearFormat : Command;

/// <summary>Ctrl+= (+1, towards H1) and Ctrl+- (-1, towards paragraph).</summary>
public sealed record ChangeHeadingLevel(int Delta) : Command;

/// <summary>The block kinds the Paragraph menu toggles on the selected blocks.</summary>
public enum BlockFormat
{
    CodeBlock,
    MathBlock,
    Quote,
}

/// <summary>Ctrl+Shift+K, Ctrl+Shift+M, Ctrl+Shift+Q: turns the selected blocks into a code block, math block or quote, or back.</summary>
public sealed record ToggleBlock(BlockFormat Format) : Command;

public enum ListKind
{
    Ordered,
    Bullet,
    Task,
}

/// <summary>Ctrl+Shift+[, Ctrl+Shift+], Ctrl+Shift+X: makes the selected blocks list items, switches the list's kind, or ends the item.</summary>
public sealed record ToggleList(ListKind Kind) : Command;

/// <summary>Blocks the Insert menu adds at the caret.</summary>
public enum InsertedBlock
{
    ParagraphAbove,
    ParagraphBelow,
    HorizontalRule,
    TableOfContents,
    FrontMatter,
}

public sealed record InsertBlock(InsertedBlock Block) : Command;

/// <summary>Ctrl+D: selects the word at the caret; with a selection it does nothing (Typora, checked by the user).</summary>
public sealed record SelectWordAtCaret : Command;

/// <summary>Ctrl+Shift+D: deletes the word at the caret, or the selection.</summary>
public sealed record DeleteWord : Command;

/// <summary>Replaces the selection when it is a match of <paramref name="Query"/> (12-find-replace.md); the caret after the replacement.</summary>
public sealed record ReplaceMatch(Search.SearchQuery Query, string Replacement) : Command;

/// <summary>Replaces every match of <paramref name="Query"/> in the document: one undo item (12-find-replace.md).</summary>
public sealed record ReplaceAllMatches(Search.SearchQuery Query, string Replacement) : Command;

/// <summary>Ctrl+E (Q39): the table cell, or the styled element around the selection, growing when repeated.</summary>
public sealed record SelectScope : Command;

/// <summary>Ctrl+L (Q39): the table row, or the line at the caret.</summary>
public sealed record SelectLine : Command;

/// <summary>Ctrl+click on a footnote reference: the caret goes to the start of its definition's text (07).</summary>
public sealed record GoToFootnoteDefinition(string Label) : Command;

/// <summary>A footnote backlink: the caret goes to the reference with that index in document order (07).</summary>
public sealed record GoToFootnoteReference(string Label, int Index) : Command;

/// <summary>Ctrl+click on a link to <c>#fragment</c>: the caret goes to the heading with that anchor (Q38).</summary>
public sealed record GoToAnchor(string Fragment) : Command;

/// <summary>Turns the block at the caret into a heading of the level, or into a paragraph for level 0 (Ctrl+1…6, Ctrl+0).</summary>
public sealed record SetHeadingLevel(int Level) : Command;

public sealed record ToggleTaskItem(BlockId Item) : Command;

public sealed record SetCodeLanguage(BlockId Block, string Info) : Command;

/// <summary>
/// The file changed on disk and is read again (M6.4): <paramref name="Reloaded"/>, imported with the
/// session's ID source, replaces the document as one undo item (Ctrl+Z goes back to the text before the
/// reload, as Typora). The caret keeps its place: the same leaf in document order, its offset clamped.
/// </summary>
public sealed record ReloadDocument(Documents.Document Reloaded) : Command;

/// <summary>A structural edit of the table at the caret (Q76, Q77).</summary>
public enum TableEdit
{
    InsertRowAbove,
    InsertRowBelow,
    InsertColumnLeft,
    InsertColumnRight,
    MoveRowUp,
    MoveRowDown,
    MoveColumnLeft,
    MoveColumnRight,
    DeleteRow,
    DeleteColumn,
    DeleteTable,

    /// <summary>Pads the table's source to aligned columns; the content does not change.</summary>
    FormatSource,
}

/// <summary>Edits the structure of the table holding the caret; outside a table it does nothing.</summary>
public sealed record EditTable(TableEdit Edit) : Command;

/// <summary>Sets the alignment of the caret's column; the current alignment again resets it to none〔默认〕.</summary>
public sealed record SetColumnAlignment(TableAlignment Alignment) : Command;

/// <summary>Resizes the table at the caret to a number of columns and rows (the header row included, Q78).</summary>
public sealed record ResizeTable(int Columns, int Rows) : Command;

public sealed record Undo : Command;

public sealed record Redo : Command;

/// <summary>Content read from the clipboard: Quill's private Markdown format when present, and plain text.</summary>
public sealed record ClipboardContent(string? Markdown, string PlainText);

/// <summary>Ctrl+V: pastes the private Markdown format when present, the plain text otherwise, parsed as Markdown.</summary>
public sealed record Paste(ClipboardContent Content) : Command;

/// <summary>Ctrl+Shift+V: pastes the plain text, parsed as Markdown without escaping (Q20).</summary>
public sealed record PasteAsPlainText(string Text) : Command;

/// <summary>Deletes the selection after it has been copied.</summary>
public sealed record Cut : Command;

/// <summary>
/// Commits the paragraph at the caret: re-parses it and replaces it when it no longer is a single
/// paragraph (02-document-model.md). Sent before saving.
/// </summary>
public sealed record CommitParagraph : Command;

/// <summary>Starts an IME composition at the selection.</summary>
public sealed record CompositionStart : Command;

/// <summary>
/// Replaces source text during a composition (CoreTextEditContext.TextUpdating) and places the caret
/// at <see cref="CaretOffset"/> in the same block.
/// </summary>
public sealed record ReplaceRange(BlockId Block, int From, int To, string Text, int CaretOffset) : Command;

/// <summary>Ends the composition, keeping its text, or removing it when <see cref="Cancel"/>.</summary>
public sealed record CompositionEnd(bool Cancel = false) : Command;

/// <summary>Changes interaction input: focus, pointer state, hover.</summary>
public sealed record SetInteraction(Interaction Interaction) : Command;
