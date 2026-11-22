using Markdig.Parsers;
using Markdig.Syntax;

namespace Quill.Editor.Markdown.Parsing;

/// <summary>
/// The "[toc]" marker on a line of its own (Typora's table of contents).
/// </summary>
internal sealed class TocMarkerBlock : LeafBlock
{
    public TocMarkerBlock(BlockParser parser)
        : base(parser)
    {
        ProcessInlines = false;
    }
}

/// <summary>
/// Parses "[toc]" (case-insensitive) with optional surrounding spaces as a single-line block. It does
/// not interrupt a paragraph: there the line is a paragraph continuation.
/// </summary>
internal sealed class TocBlockParser : BlockParser
{
    private const string Marker = "[toc]";

    public TocBlockParser()
    {
        OpeningCharacters = ['['];
    }

    public override BlockState TryOpen(BlockProcessor processor)
    {
        if (processor.IsCodeIndent || processor.CurrentBlock is ParagraphBlock)
        {
            return BlockState.None;
        }

        var line = processor.Line;
        var rest = line.Text.AsSpan(line.Start, line.Length);
        if (!rest.StartsWith(Marker, StringComparison.OrdinalIgnoreCase)
            || !rest[Marker.Length..].Trim(" \t").IsEmpty)
        {
            return BlockState.None;
        }

        processor.NewBlocks.Push(new TocMarkerBlock(this)
        {
            Line = processor.LineIndex,
            Column = processor.Column,
            Span = new SourceSpan(processor.Start, line.End),
        });
        return BlockState.BreakDiscard;
    }
}
