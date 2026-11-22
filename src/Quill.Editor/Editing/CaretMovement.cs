using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Services;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Resolves <see cref="MoveCaret"/> to a selection (04-commands-and-behaviors.md). Units that depend
/// on layout (visual lines, pages, visual line ends) go through <see cref="ILayoutService"/>; without
/// one they fall back to source lines. Only vertical moves keep a goal X.
/// </summary>
internal static class CaretMovement
{
    public static (TextSelection Selection, double? GoalX) Move(EditorState state, MoveCaret move, ILayoutService? layout)
    {
        var document = state.Document;
        var selection = state.Selection;
        bool forward = move.Direction is MoveDirection.Forward or MoveDirection.Down;
        double? goalX = null;
        Position target;

        if (selection.IsBlockCaret)
        {
            return FromBlockCaret(state, move, layout, forward);
        }

        if (move.Direction is MoveDirection.Backward or MoveDirection.Forward
            && move.Unit is TextUnit.Grapheme or TextUnit.Word && !move.Extend && !selection.IsCollapsed)
        {
            // An arrow key collapses a selection to its start or end.
            var (start, end) = PositionOrder.Ordered(document, selection);
            return (TextSelection.Caret(forward ? end : start), null);
        }

        var head = selection.Head;
        switch (move.Unit)
        {
            case TextUnit.Grapheme or TextUnit.Word when move.Direction is MoveDirection.Backward or MoveDirection.Forward:
                target = CaretStops.Next(document, head, forward, move.Unit == TextUnit.Word) ?? head;
                break;

            case TextUnit.LineBoundary:
                target = layout is not null
                    ? (forward ? layout.GetVisualLine(head).End : layout.GetVisualLine(head).Start)
                    : SourceLineEdge(document, head, forward);
                break;

            case TextUnit.Document:
                target = (forward ? CaretStops.Edge(document, forward: false) : CaretStops.Edge(document, forward: true)) ?? head;
                break;

            case TextUnit.Paragraph:
                target = ParagraphEdge(document, head, forward);
                break;

            default:
                (target, goalX) = Vertical(state, head, forward, move.Unit == TextUnit.Page, layout);
                if (!move.Extend && BlockCarets.Between(document, head, target, forward) is { } blockCaret)
                {
                    // Up and Down stop on a TOC or a rule on the way (Q59, Q60).
                    return (blockCaret, goalX);
                }

                break;
        }

        return (new TextSelection(move.Extend ? selection.Anchor : target, target), goalX);
    }

    /// <summary>
    /// Moves from a block caret (Q59): arrows and page keys leave it in their direction, extending
    /// moves select from the far side of the block, Ctrl+Home / Ctrl+End go to the document's ends;
    /// Home and End have no line to move in.
    /// </summary>
    private static (TextSelection Selection, double? GoalX) FromBlockCaret(EditorState state, MoveCaret move, ILayoutService? layout, bool forward)
    {
        var caret = state.Selection;
        switch (move.Unit)
        {
            case TextUnit.LineBoundary:
                return (caret, state.GoalX);
            case TextUnit.Document:
                return Move(state with { Selection = TextSelection.Caret(forward ? caret.Head : caret.Anchor) }, move, layout);
        }

        if (move.Extend)
        {
            var from = state with { Selection = new TextSelection(forward ? caret.Anchor : caret.Head, forward ? caret.Head : caret.Anchor) };
            return Move(from, move, layout);
        }

        var left = BlockCarets.Leave(state.Document, caret, forward);
        if (left is { IsCollapsed: true, Head: TextPosition } && move.Unit is TextUnit.VisualLine && layout is not null && state.GoalX is { } goalX
            && layout.GetAdjacentLine(forward ? caret.Head : caret.Anchor, goalX, forward) is { } line)
        {
            // Leaving onto text by Up or Down keeps the goal X: the line next to the block at it (M5.2).
            return (BlockCarets.Between(state.Document, forward ? caret.Head : caret.Anchor, line, forward) ?? TextSelection.Caret(line), goalX);
        }

        return (left ?? caret, state.GoalX);
    }

    /// <summary>Up and Down: through the layout, keeping the goal X; by source lines without a layout.</summary>
    private static (Position Target, double? GoalX) Vertical(EditorState state, Position head, bool down, bool page, ILayoutService? layout)
    {
        var document = state.Document;
        if (layout is null)
        {
            return (SourceLineVertical(document, head, down), null);
        }

        var caret = layout.GetCaretRect(head);
        double x = state.GoalX ?? caret.X;
        Position? target;
        if (page)
        {
            double distance = layout.GetViewport().Height;
            target = layout.HitTest(new LayoutPoint(x, down ? caret.Bottom + distance : caret.Y - distance));
        }
        else
        {
            target = layout.GetAdjacentLine(head, x, down);
        }

        if (target is null || target.SamePlace(head))
        {
            // Past the first or last line: to the start or end of the document.
            target = (down ? CaretStops.Edge(document, forward: false) : CaretStops.Edge(document, forward: true)) ?? head;
        }

        // Up or Down into the language of a code block lands at its end (Q91), whichever way it came.
        if (target is TextPosition into && (head as TextPosition)?.Block != into.Block
            && DocumentTree.TryFind(document, into.Block) is CodeLanguage language)
        {
            target = new TextPosition(language.Id, language.Text.Length, Affinity.Backward);
        }

        return (target, x);
    }

    private static Position SourceLineEdge(Document document, Position head, bool forward)
    {
        if (head is not TextPosition text)
        {
            return head;
        }

        string source = DocumentTree.Find<LeafBlock>(document, text.Block).Text;
        int offset = forward
            ? (source.IndexOf('\n', text.Offset) is >= 0 and var end ? end : source.Length)
            : (text.Offset == 0 ? 0 : source.LastIndexOf('\n', text.Offset - 1) + 1);
        return text with { Offset = offset, Affinity = forward ? Affinity.Backward : Affinity.Forward };
    }

    /// <summary>Without layout: the same column of the previous or next source line, or the next stop across blocks.</summary>
    private static Position SourceLineVertical(Document document, Position head, bool down)
    {
        if (head is not TextPosition text)
        {
            return CaretStops.Next(document, head, down) ?? head;
        }

        string source = DocumentTree.Find<LeafBlock>(document, text.Block).Text;
        int lineStart = text.Offset == 0 ? 0 : source.LastIndexOf('\n', text.Offset - 1) + 1;
        int column = text.Offset - lineStart;
        if (down)
        {
            int lineEnd = source.IndexOf('\n', text.Offset);
            if (lineEnd < 0)
            {
                return CaretStops.Next(document, text with { Offset = source.Length }, forward: true) ?? head;
            }

            int nextEnd = source.IndexOf('\n', lineEnd + 1) is >= 0 and var end ? end : source.Length;
            return text with { Offset = Math.Min(lineEnd + 1 + column, nextEnd) };
        }

        if (lineStart == 0)
        {
            return CaretStops.Next(document, text with { Offset = 0 }, forward: false) ?? head;
        }

        int previousStart = lineStart == 1 ? 0 : source.LastIndexOf('\n', lineStart - 2) + 1;
        return text with { Offset = Math.Min(previousStart + column, lineStart - 1) };
    }

    /// <summary>Ctrl+Up / Ctrl+Down: the start of the current block, or of the previous or next one.</summary>
    private static Position ParagraphEdge(Document document, Position head, bool forward)
    {
        if (head is TextPosition { Offset: > 0 } text && !forward)
        {
            return text with { Offset = 0 };
        }

        var next = CaretStops.Next(document, head is TextPosition t && forward
            ? t with { Offset = DocumentTree.Find<LeafBlock>(document, t.Block).Text.Length }
            : head, forward);
        if (next is TextPosition { } across && !forward)
        {
            return across with { Offset = 0 };
        }

        return next ?? head;
    }
}
