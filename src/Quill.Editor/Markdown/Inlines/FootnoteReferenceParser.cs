using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Quill.Editor.Markdown.Inlines;

/// <summary>A footnote reference <c>[^label]</c>.</summary>
internal sealed class FootnoteReferenceInline : LeafInline
{
    /// <summary>The label without the leading '^'.</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Parses <c>[^label]</c> as a footnote reference whether or not the footnote is defined: Typora shows
/// an unresolved reference as a reference with a warning (10-elements.md). Markdig's footnote extension
/// only recognizes defined labels, and Quill parses definitions itself anyway. A reference followed by
/// '(' is left to the link parser, as in Markdig.
/// </summary>
internal sealed class FootnoteReferenceParser : InlineParser
{
    public FootnoteReferenceParser()
    {
        OpeningCharacters = ['['];
    }

    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.PeekChar() != '^')
        {
            return false;
        }

        int start = slice.Start;
        var rest = slice;
        if (!LinkHelper.TryParseLabel(ref rest, false, out string? label, out _)
            || label is null
            || label.Length < 2
            || label[0] != '^'
            || rest.CurrentChar == '(')
        {
            return false;
        }

        int position = processor.GetSourcePosition(start, out int line, out int column);
        processor.Inline = new FootnoteReferenceInline
        {
            Label = label[1..],
            Span = new SourceSpan(position, position + (rest.Start - start) - 1),
            Line = line,
            Column = column,
        };
        slice = rest;
        return true;
    }
}
