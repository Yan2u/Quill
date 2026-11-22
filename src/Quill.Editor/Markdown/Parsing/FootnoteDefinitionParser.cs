using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Quill.Editor.Markdown.Parsing;

/// <summary>
/// A footnote definition parsed in place. Markdig's own footnote extension moves definitions into a
/// group at the end of the document and drops the ones nobody references, which loses source order
/// and content, so Quill parses them with <see cref="FootnoteDefinitionParser"/> instead.
/// </summary>
internal sealed class FootnoteDefinitionBlock : ContainerBlock
{
    public FootnoteDefinitionBlock(BlockParser parser)
        : base(parser)
    {
    }

    /// <summary>The label including the leading '^'.</summary>
    public string Label { get; set; } = string.Empty;

    public SourceSpan LabelSpan { get; set; }

    public bool IsLastLineEmpty { get; set; }
}

/// <summary>
/// Parses "[^label]: content" at document level. The continuation rules follow Markdig's
/// FootnoteParser (BSD-2-Clause, copyright Alexandre Mutel): an unindented line ends the footnote after
/// a blank line or when it starts another definition; indented lines continue it.
/// </summary>
internal sealed class FootnoteDefinitionParser : BlockParser
{
    public FootnoteDefinitionParser()
    {
        OpeningCharacters = ['['];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        // Like Markdig, only at document level and not indented as code.
        if (processor.IsCodeIndent || processor.GetCurrentContainerOpened() is not MarkdownDocument)
        {
            return BlockState.None;
        }

        int savedColumn = processor.Column;
        int start = processor.Start;
        if (!LinkHelper.TryParseLabel(ref processor.Line, false, out string? label, out SourceSpan labelSpan)
            || !label.StartsWith('^')
            || processor.CurrentChar != ':')
        {
            processor.GoToColumn(savedColumn);
            return BlockState.None;
        }

        processor.Column += processor.Start - start;
        processor.NextChar(); // Skip ':'.

        var footnote = new FootnoteDefinitionBlock(this)
        {
            Label = label,
            LabelSpan = labelSpan,
            Line = processor.LineIndex,
            Column = savedColumn,
            Span = new SourceSpan(start, processor.Line.End),
        };

        // The rest of the definition line is the footnote's text even where it reads as block syntax:
        // Typora's description slot is inline text (Q44). The paragraph goes on the stack first so
        // that it opens inside the footnote, and takes the rest of the line.
        if (!processor.Line.IsEmptyOrWhitespace() && processor.Parsers.Find<ParagraphBlockParser>() is { } paragraphParser)
        {
            processor.NewBlocks.Push(new ParagraphBlock(paragraphParser)
            {
                Column = processor.Column,
                Span = new SourceSpan(processor.Start, processor.Line.End),
            });
        }

        processor.NewBlocks.Push(footnote);
        return BlockState.Continue;
    }

    public override BlockState TryContinue(BlockProcessor processor, Block block)
    {
        var footnote = (FootnoteDefinitionBlock)block;

        if (processor.CurrentBlock is null || processor.CurrentBlock.IsBreakable)
        {
            if (processor.IsBlankLine)
            {
                footnote.IsLastLineEmpty = true;
                return BlockState.ContinueDiscard;
            }

            // An unindented line after a blank line, or one that starts another definition, ends the
            // footnote; returning None closes it and lets the line open new blocks.
            if (processor.Column == 0 && (footnote.IsLastLineEmpty || StartsDefinition(processor.Line)))
            {
                return BlockState.None;
            }
        }

        footnote.IsLastLineEmpty = false;
        if (processor.IsCodeIndent)
        {
            processor.GoToCodeIndent();
        }

        return BlockState.Continue;
    }

    private static bool StartsDefinition(StringSlice line)
    {
        return LinkHelper.TryParseLabel(ref line, false, out string? label, out _)
            && label.StartsWith('^')
            && line.CurrentChar == ':';
    }
}
