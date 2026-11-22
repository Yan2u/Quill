using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Md = Markdig.Syntax;
using MdMath = Markdig.Extensions.Mathematics;
using MdYaml = Markdig.Extensions.Yaml;

namespace Quill.Editor.Markdown.Import;

internal sealed partial class BlockConverter
{
    private Converted ConvertParagraph(Md.ParagraphBlock paragraph, ImmutableArray<ChainEntry> chain)
    {
        var (first, last) = LeafLines(paragraph);
        var (text, prefixes) = ReadLines(first, last, chain, lazyFrom: first + 1);
        return new Converted(new Paragraph { Id = _ids.NextId(), Text = text, LinePrefixes = prefixes }, first, last);
    }

    private Converted ConvertHtml(Md.HtmlBlock html, ImmutableArray<ChainEntry> chain)
    {
        var (first, last) = LeafLines(html);
        var (text, prefixes) = ReadLines(first, last, chain);
        return new Converted(new HtmlBlock { Id = _ids.NextId(), Text = text, LinePrefixes = prefixes }, first, last);
    }

    private IEnumerable<Converted> ConvertHeading(Md.HeadingBlock heading, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        if (heading.IsSetext)
        {
            var (first, last) = LeafLines(heading);
            int underlineLine = Math.Max(_source.LineOf(heading.Span.End), last + 1);
            var result = new List<Converted>(2);
            int headingLine = first;
            if (last > first)
            {
                // Headings never span lines (Typora): of a multi-line Setext heading only the last line
                // is the heading; the lines above it stay a paragraph, adjacent to it.
                var (paragraphText, paragraphPrefixes) = ReadLines(first, last - 1, chain, lazyFrom: first + 1);
                result.Add(new Converted(new Paragraph { Id = _ids.NextId(), Text = paragraphText, LinePrefixes = paragraphPrefixes }, first, last - 1));
                headingLine = last;
            }

            var (text, prefixes) = ReadLines(headingLine, headingLine, chain, lazyFrom: headingLine > first ? headingLine : int.MaxValue);

            // CommonMark trims a heading's content; keep the outer whitespace as syntax, as for ATX.
            int leading = text.Length - text.TrimStart(" \t".ToCharArray()).Length;
            int trailing = text.Length - leading - text[leading..].TrimEnd(" \t".ToCharArray()).Length;
            var scan = LineScanner.Scan(_source, underlineLine, chain);
            string headingText = text[leading..(text.Length - trailing)];
            var setext = new Heading
            {
                Id = id,
                Level = heading.Level,
                Text = headingText,
                LinePrefixes = prefixes,
                Syntax = new HeadingSyntax
                {
                    Style = HeadingStyle.Setext,
                    Level = heading.Level,
                    Opening = text[..leading],
                    Closing = text[(text.Length - trailing)..],
                    Underline = new SyntaxLine(BodyText(underlineLine, scan), scan.Prefix),
                    SetextText = headingText,
                },
            };
            result.Add(new Converted(setext, headingLine, underlineLine));
            return result;
        }

        int line = heading.Line;
        var lineScan = LineScanner.Scan(_source, line, chain);
        var (opening, content, closing) = AtxHeading.Split(BodyText(line, lineScan));
        var atx = new Heading
        {
            Id = id,
            Level = heading.Level,
            Text = content,
            LinePrefixes = CompactPrefixes([lineScan.Prefix]),
            Syntax = new HeadingSyntax { Style = HeadingStyle.Atx, Level = heading.Level, Opening = opening, Closing = closing },
        };
        return [new Converted(atx, line, line)];
    }

    private Converted ConvertAtomic(Md.Block block, ImmutableArray<ChainEntry> chain, Func<BlockId, SyntaxLine, AtomicBlock> create)
    {
        int line = block.Line;
        var scan = LineScanner.Scan(_source, line, chain);
        return new Converted(create(_ids.NextId(), new SyntaxLine(BodyText(line, scan), scan.Prefix)), line, line);
    }

    private Converted ConvertFencedCode(Md.FencedCodeBlock code, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var fence = ReadFence(code, chain, id, code.FencedChar, code.OpeningFencedCharCount, code.IndentCount,
            closed: code.ClosingFencedCharCount > 0, out var text, out var prefixes, out var lastLine);
        string info = ParseInfo(fence.Opening.Text, fence.FenceChar);
        var block = new CodeFence
        {
            Id = _ids.NextId(),
            Syntax = new CodeBlockSyntax { Style = CodeBlockStyle.Fenced, Fence = fence with { Info = info } },
            Children = [new CodeBlock { Id = id, Text = text, LinePrefixes = prefixes }, new CodeLanguage { Id = _ids.NextId(), Text = info }],
        };
        return new Converted(block, code.Line, lastLine);
    }

    private Converted ConvertMathBlock(MdMath.MathBlock math, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var fence = ReadFence(math, chain, id, math.FencedChar, math.OpeningFencedCharCount, math.IndentCount,
            closed: math.ClosingFencedCharCount > 0, out var text, out var prefixes, out var lastLine);
        var block = new MathBlock { Id = id, Text = text, LinePrefixes = prefixes, Syntax = fence };
        return new Converted(block, math.Line, lastLine);
    }

    private Converted ConvertFrontMatter(MdYaml.YamlFrontMatterBlock frontMatter, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var fence = ReadFence(frontMatter, chain, id, '-', 3, indentColumns: 0, closed: true,
            out var text, out var prefixes, out var lastLine);
        var block = new FrontMatter { Id = id, Text = text, LinePrefixes = prefixes, Syntax = fence };
        return new Converted(block, frontMatter.Line, lastLine);
    }

    /// <summary>
    /// Reads the opening line, the content lines and the closing line of a fenced block. Up to
    /// <paramref name="indentColumns"/> columns of indentation are removed from content lines and kept
    /// as a prefix segment owned by the block itself.
    /// </summary>
    private FenceSyntax ReadFence(
        Md.LeafBlock block,
        ImmutableArray<ChainEntry> chain,
        BlockId ownerId,
        char fenceChar,
        int fenceLength,
        int indentColumns,
        bool closed,
        out string text,
        out ImmutableArray<LinePrefix> prefixes,
        out int lastLine)
    {
        int openLine = block.Line;
        var openScan = LineScanner.Scan(_source, openLine, chain);
        int closeLine = closed ? Math.Max(_source.LineOf(block.Span.End), openLine + 1) : -1;
        int lastContent = closed
            ? closeLine - 1
            : (block.Lines.Count > 0 ? block.Lines.Lines[block.Lines.Count - 1].Line : openLine);

        var bodies = new List<string>();
        var linePrefixes = new List<LinePrefix>();
        for (int line = openLine + 1; line <= lastContent; line++)
        {
            var scan = LineScanner.Scan(_source, line, chain);
            var cursor = scan.Body;
            int ownStart = cursor.Position;
            cursor.ConsumeWhitespace(indentColumns);
            var prefix = scan.Prefix;
            if (indentColumns > 0)
            {
                prefix = Append(prefix, new PrefixSegment(ownerId, _source.Slice(ownStart, cursor.Position)));
            }

            bodies.Add(RestOfLine(line, cursor));
            linePrefixes.Add(prefix);
        }

        SyntaxLine? closing = null;
        if (closed)
        {
            var closeScan = LineScanner.Scan(_source, closeLine, chain);
            closing = new SyntaxLine(BodyText(closeLine, closeScan), closeScan.Prefix);
        }

        text = string.Join('\n', bodies);
        prefixes = CompactPrefixes(linePrefixes);
        lastLine = closed ? closeLine : lastContent;
        return new FenceSyntax
        {
            Opening = new SyntaxLine(BodyText(openLine, openScan), openScan.Prefix),
            Closing = closing,
            FenceChar = fenceChar,
            FenceLength = fenceLength,
            IndentColumns = indentColumns,
            HasNoContentLines = bodies.Count == 0,
            UnclosedText = closed ? null : text,
        };
    }

    private Converted ConvertIndentedCode(Md.CodeBlock code, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var (first, last) = LeafLines(code);
        var bodies = new List<string>();
        var linePrefixes = new List<LinePrefix>();
        for (int line = first; line <= last; line++)
        {
            var scan = LineScanner.Scan(_source, line, chain);
            var cursor = scan.Body;
            int ownStart = cursor.Position;
            cursor.ConsumeWhitespace(4);
            linePrefixes.Add(Append(scan.Prefix, new PrefixSegment(id, _source.Slice(ownStart, cursor.Position))));
            bodies.Add(RestOfLine(line, cursor));
        }

        var block = new CodeFence
        {
            Id = _ids.NextId(),
            Syntax = new CodeBlockSyntax { Style = CodeBlockStyle.Indented },
            Children = [new CodeBlock { Id = id, Text = string.Join('\n', bodies), LinePrefixes = [.. linePrefixes] }, new CodeLanguage { Id = _ids.NextId(), Text = string.Empty }],
        };
        return new Converted(block, first, last);
    }

    /// <summary>The info string: the opening line after the fence, trimmed of spaces and tabs.</summary>
    private static string ParseInfo(string openingLine, char fenceChar)
    {
        var rest = openingLine.AsSpan().TrimStart(' ').TrimStart(fenceChar);
        return rest.Trim(" \t").ToString();
    }

    /// <summary>The physical lines of a leaf as recorded by Markdig.</summary>
    private (int First, int Last) LeafLines(Md.LeafBlock leaf)
    {
        if (leaf.Lines.Count > 0)
        {
            return (leaf.Lines.Lines[0].Line, leaf.Lines.Lines[leaf.Lines.Count - 1].Line);
        }

        int last = leaf.Span.End >= leaf.Span.Start ? _source.LineOf(leaf.Span.End) : leaf.Line;
        return (leaf.Line, Math.Max(leaf.Line, last));
    }

    private static LinePrefix Append(LinePrefix prefix, PrefixSegment segment)
    {
        return new LinePrefix(prefix.IsNone ? [segment] : prefix.Segments.Add(segment));
    }
}
