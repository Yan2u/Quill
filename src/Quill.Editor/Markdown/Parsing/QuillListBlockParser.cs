using System.Runtime.CompilerServices;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Quill.Editor.Markdown.Parsing;

/// <summary>
/// Markdig's list parser with the changes that make lists read as CommonMark specifies where Markdig
/// differs, and as Typora reads them where Typora differs:
/// <list type="bullet">
/// <item>A line indented to an item's content continues the item, which takes exactly its content
/// width from the line (CommonMark). Markdig takes all the indentation unless it is a code indent,
/// so items nested inside measured their indentation from varying columns; and it closes the list on
/// any thematic break line, while a rule indented into an item stays in it (Typora too, Q47).</item>
/// <item>A marker must be followed by a space or tab: a marker alone on its line is text (Typora, Q52).
/// See <see cref="TyporaBulletItemParser"/> and <see cref="TyporaNumberedItemParser"/>.</item>
/// <item>An item whose marker line is empty does not end at its first blank line: content indented to
/// the item after blank lines stays in it, so an emptied item keeps its headless items (Typora, Q53).
/// CommonMark lets an item begin with at most one blank line; Markdig also ends such an item at a
/// blank line after its content.</item>
/// <item>An empty item may interrupt a paragraph, as in "- a" followed by "    - " (Typora, Q56).
/// CommonMark forbids it, which makes the marker line paragraph text or a Setext underline.</item>
/// </list>
/// </summary>
internal sealed class QuillListBlockParser : ListBlockParser
{
    public QuillListBlockParser()
    {
        ItemParsers.Clear();
        ItemParsers.Add(new TyporaBulletItemParser());
        ItemParsers.Add(new TyporaNumberedItemParser());
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        // Markdig refuses an empty item right after an open paragraph unless the line has unmatched
        // containers; the empty item is let through by marking the line so for the attempt.
        if (processor.LastBlock is ParagraphBlock && !GetHasUnmatchedBlocks(processor) && IsEmptyItemThatMayInterrupt(processor.Line))
        {
            SetHasUnmatchedBlocks(processor, true);
            try
            {
                return base.TryOpen(processor);
            }
            finally
            {
                SetHasUnmatchedBlocks(processor, false);
            }
        }

        return base.TryOpen(processor);
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        if (block is not ListItemBlock item || item.Parent is not ListBlock list)
        {
            return base.TryContinue(processor, block);
        }

        int width = Math.Abs(item.ColumnWidth);
        if (processor.IsBlankLine && item.ColumnWidth < 0)
        {
            // What Markdig does for a blank line in any other item.
            if (processor.CurrentBlock is { IsBreakable: true })
            {
                if (processor.NextContinue is not ListBlock)
                {
                    SetCountAllBlankLines(list, GetCountAllBlankLines(list) + 1);
                    if (!processor.TrackTrivia)
                    {
                        item.Add(new BlankLineBlock());
                    }
                }

                SetCountBlankLinesReset(list, GetCountBlankLinesReset(list) + 1);
            }

            item.UpdateSpanEnd(processor.Line.End);
            return BlockState.Continue;
        }

        if (!processor.IsBlankLine && processor.Indent >= width)
        {
            // What Markdig does for a line indented into the item, taking exactly the item's width.
            SetCountBlankLinesReset(list, 0);
            processor.GoToColumn(processor.ColumnBeforeIndent + width);
            item.UpdateSpanEnd(processor.Line.End);
            item.NewLine = processor.Line.NewLine;
            return BlockState.Continue;
        }

        return base.TryContinue(processor, block);
    }

    /// <summary>
    /// A bullet, or the number 1 and its delimiter, followed by spaces or tabs and nothing else. An
    /// ordered list starting at another number still cannot interrupt a paragraph (CommonMark).
    /// </summary>
    private static bool IsEmptyItemThatMayInterrupt(StringSlice line)
    {
        int position = line.Start;
        string text = line.Text;
        int end = line.End;
        if (position > end)
        {
            return false;
        }

        if (text[position] is '-' or '+' or '*')
        {
            position++;
        }
        else
        {
            int digitsStart = position;
            int number = 0;
            while (position <= end && char.IsAsciiDigit(text[position]) && position - digitsStart < 9)
            {
                number = (number * 10) + (text[position] - '0');
                position++;
            }

            if (position == digitsStart || number != 1 || position > end || text[position] is not ('.' or ')'))
            {
                return false;
            }

            position++;
        }

        if (position > end || text[position] is not (' ' or '\t'))
        {
            return false;
        }

        for (; position <= end; position++)
        {
            if (text[position] is not (' ' or '\t'))
            {
                return false;
            }
        }

        return true;
    }

    // Markdig's counters of blank lines in a list and its flag for lines with unmatched containers
    // are internal, so they are reached the supported way for internal members.
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_CountAllBlankLines")]
    private static extern int GetCountAllBlankLines(ListBlock list);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CountAllBlankLines")]
    private static extern void SetCountAllBlankLines(ListBlock list, int value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_CountBlankLinesReset")]
    private static extern int GetCountBlankLinesReset(ListBlock list);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_CountBlankLinesReset")]
    private static extern void SetCountBlankLinesReset(ListBlock list, int value);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_HasUnmatchedBlocks")]
    private static extern bool GetHasUnmatchedBlocks(BlockProcessor processor);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_HasUnmatchedBlocks")]
    private static extern void SetHasUnmatchedBlocks(BlockProcessor processor, bool value);
}

/// <summary>A bullet ('-', '+', '*') that is not alone on its line (Typora).</summary>
internal sealed class TyporaBulletItemParser : UnorderedListItemParser
{
    public override bool TryParse(BlockProcessor state, char pendingBulletType, out ListInfo result)
    {
        // The caller restores the position when parsing fails.
        return base.TryParse(state, pendingBulletType, out result) && state.CurrentChar != '\0';
    }
}

/// <summary>A number and '.' or ')' that is not alone on its line (Typora).</summary>
internal sealed class TyporaNumberedItemParser : NumberedListItemParser
{
    public override bool TryParse(BlockProcessor state, char pendingBulletType, out ListInfo result)
    {
        // The caller restores the position when parsing fails.
        return base.TryParse(state, pendingBulletType, out result) && state.CurrentChar != '\0';
    }
}
