using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.Markdown.Parsing;
using Md = Markdig.Syntax;
using MdMath = Markdig.Extensions.Mathematics;
using MdTables = Markdig.Extensions.Tables;
using MdYaml = Markdig.Extensions.Yaml;

namespace Quill.Editor.Markdown.Import;

/// <summary>
/// A converted block and the physical lines it owns.
/// </summary>
internal readonly record struct Converted(Block Block, int FirstLine, int LastLine);

/// <summary>
/// Converts Markdig's block tree into the document model. Markdig supplies structure and positions;
/// every character of every physical line is assigned here, either to a leaf's text or to syntax
/// metadata (06-markdown-io.md), so that serializing an unedited document reproduces it exactly.
/// </summary>
internal sealed partial class BlockConverter
{
    private readonly SourceText _source;
    private readonly IBlockIdSource _ids;
    private readonly List<string> _diagnostics = [];
    private readonly Dictionary<Md.ContainerBlock, List<Md.Block>> _rehomed = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Md.Block, int> _lastLines = new(ReferenceEqualityComparer.Instance);

    public BlockConverter(SourceText source, IBlockIdSource ids)
    {
        _source = source;
        _ids = ids;
    }

    public IReadOnlyList<string> Diagnostics => _diagnostics;

    public ImmutableList<Block> ConvertDocument(Md.MarkdownDocument markdown)
    {
        RehomeDefinitions(markdown);
        var chain = ImmutableArray<ChainEntry>.Empty;
        var children = ConvertChildren(markdown, chain);
        return ConvertBody(children, 0, _source.LineCount - 1, chain, previousIsContent: false, BodyKind.Blocks);
    }

    private List<Converted> ConvertChildren(Md.ContainerBlock container, ImmutableArray<ChainEntry> chain)
    {
        var result = new List<Converted>();
        foreach (var child in OrderedChildren(container))
        {
            result.AddRange(ConvertBlock(child, chain));
        }

        result.Sort((left, right) => left.FirstLine.CompareTo(right.FirstLine));
        return result;
    }

    private IEnumerable<Converted> ConvertBlock(Md.Block block, ImmutableArray<ChainEntry> chain)
    {
        switch (block)
        {
            case MdMath.MathBlock math:
                return [ConvertMathBlock(math, chain)];
            case MdYaml.YamlFrontMatterBlock frontMatter:
                return [ConvertFrontMatter(frontMatter, chain)];
            case Md.FencedCodeBlock fenced:
                return [ConvertFencedCode(fenced, chain)];
            case Md.CodeBlock indented:
                return [ConvertIndentedCode(indented, chain)];
            case Md.HeadingBlock heading:
                return ConvertHeading(heading, chain);
            case Md.ParagraphBlock paragraph:
                return [ConvertParagraph(paragraph, chain)];
            case Md.ThematicBreakBlock:
                return [ConvertAtomic(block, chain, static (id, syntax) => new ThematicBreak { Id = id, Syntax = syntax })];
            case TocMarkerBlock:
                return [ConvertAtomic(block, chain, static (id, syntax) => new TocBlock { Id = id, Syntax = syntax })];
            case Md.HtmlBlock html:
                return [ConvertHtml(html, chain)];
            case Md.LinkReferenceDefinition definition:
                return [ConvertLinkDefinition(definition, chain)];
            case Md.QuoteBlock quote:
                return [ConvertQuote(quote, chain)];
            case Md.ListBlock list:
                return [ConvertList(list, chain)];
            case MdTables.Table table:
                return [ConvertTable(table, chain)];
            case FootnoteDefinitionBlock footnote:
                return [ConvertFootnote(footnote, chain)];
            default:
                int first = MdFirstLine(block);
                int last = MdLastLine(block);
                _diagnostics.Add($"Unsupported block {block.GetType().Name} on lines {first}-{last}; kept as raw text.");
                return [ConvertRawLines(first, last, chain)];
        }
    }

    /// <summary>What the gap lines of a body may become.</summary>
    private enum BodyKind
    {
        /// <summary>Document, block quote, footnote: extra blank lines become empty paragraphs.</summary>
        Blocks,

        /// <summary>
        /// A list item: as <see cref="Blocks"/>, and a blank line indented to the item's content after
        /// another blank line is an empty paragraph of its own, with the lines before it as its
        /// separator. Typora writes an empty paragraph in a list item that way (Q56).
        /// </summary>
        ItemBlocks,

        /// <summary>A list: blank lines between items stay in the next item's separator.</summary>
        ListItems,
    }

    /// <summary>
    /// Assembles a container's children from its converted blocks and the gap lines between them
    /// (lines <paramref name="firstLine"/> to <paramref name="lastLine"/> not owned by a block). Between
    /// two blocks the first blank line is the separator, recorded on the later block; every other blank
    /// line becomes an empty paragraph (02-document-model.md). The serializer applies the same rule.
    /// </summary>
    /// <param name="firstIsContent">The first block counts as content even when it is an empty paragraph (a task item's text, which shares the marker line).</param>
    private ImmutableList<Block> ConvertBody(
        IReadOnlyList<Converted> blocks,
        int firstLine,
        int lastLine,
        ImmutableArray<ChainEntry> chain,
        bool previousIsContent,
        BodyKind kind,
        bool firstIsContent = false)
    {
        var result = ImmutableList.CreateBuilder<Block>();
        var blanks = new List<SyntaxLine>();
        var rawStart = -1;
        bool hasPrevious = previousIsContent;
        Block? previous = null;

        void FlushRaw(int endLine)
        {
            if (rawStart < 0)
            {
                return;
            }

            _diagnostics.Add($"Lines {rawStart}-{endLine} are not owned by any block; kept as raw text.");
            var raw = ConvertRawLines(rawStart, endLine, chain);
            rawStart = -1;
            AddBlock(raw.Block);
        }

        void AddBlock(Block block)
        {
            BlockSeparator? separator = null;
            int emptyStart = 0;
            if (kind == BodyKind.ListItems)
            {
                separator = hasPrevious ? new BlockSeparator([.. blanks], previous?.Id) : null;
                emptyStart = blanks.Count;
            }
            else if (hasPrevious)
            {
                separator = blanks.Count == 0 ? BlockSeparator.Adjacent(previous?.Id) : new BlockSeparator([blanks[0]], previous?.Id);
                emptyStart = Math.Min(1, blanks.Count);
            }

            if (block is ListBlock { Syntax: { } syntax } list && previous is ListBlock previousList
                && previousList.IsOrdered == list.IsOrdered && previousList.MarkerChar == list.MarkerChar)
            {
                block = list with { Syntax = syntax with { SeparateFrom = previousList.Id } };
            }

            AddEmptyParagraphs(blanks, emptyStart);
            blanks.Clear();
            result.Add(block with { SeparatorBefore = separator });
            if (!IsEmptyParagraph(block) || firstIsContent)
            {
                hasPrevious = true;
                previous = block;
            }

            firstIsContent = false;
        }

        void AddIndentedEmptyParagraph(SyntaxLine line)
        {
            var paragraph = new Paragraph
            {
                Id = _ids.NextId(),
                Text = line.Text,
                LinePrefixes = line.Prefix.IsNone ? [] : [line.Prefix],
            };

            // The separator marks it as a block of its own; at the start of the container the blank
            // lines before it are empty paragraphs (the first of them the item's empty text).
            var separator = hasPrevious ? new BlockSeparator([blanks[0]], previous?.Id) : BlockSeparator.Adjacent(null);
            AddEmptyParagraphs(blanks, hasPrevious ? 1 : 0);
            blanks.Clear();
            result.Add(paragraph with { SeparatorBefore = separator });
            hasPrevious = true;
            previous = paragraph;
            firstIsContent = false;
        }

        void AddEmptyParagraphs(List<SyntaxLine> lines, int start)
        {
            for (int i = start; i < lines.Count; i++)
            {
                result.Add(new Paragraph
                {
                    Id = _ids.NextId(),
                    Text = lines[i].Text,
                    LinePrefixes = lines[i].Prefix.IsNone ? [] : [lines[i].Prefix],
                });
            }
        }

        void ConsumeGap(int from, int to)
        {
            for (int line = from; line <= to; line++)
            {
                var scan = LineScanner.Scan(_source, line, chain);
                if (scan.Body.RestIsBlank())
                {
                    FlushRaw(line - 1);
                    var blank = new SyntaxLine(BodyText(line, scan), scan.Prefix);
                    if (kind == BodyKind.ItemBlocks && scan.ReachesItemContent && blanks.Count > 0)
                    {
                        AddIndentedEmptyParagraph(blank);
                    }
                    else
                    {
                        blanks.Add(blank);
                    }
                }
                else if (rawStart < 0)
                {
                    rawStart = line;
                }
            }

            FlushRaw(to);
        }

        int next = firstLine;
        foreach (var block in blocks)
        {
            ConsumeGap(next, block.FirstLine - 1);
            AddBlock(block.Block);
            next = Math.Max(next, block.LastLine + 1);
        }

        ConsumeGap(next, lastLine);
        if (kind == BodyKind.ListItems && blanks.Count > 0)
        {
            _diagnostics.Add($"Blank lines after the last list item before line {lastLine + 1} were dropped.");
        }
        else
        {
            AddEmptyParagraphs(blanks, 0);
        }

        return result.ToImmutable();
    }

    internal static bool IsEmptyParagraph(Block block) => block is Paragraph { IsEmpty: true };

    /// <summary>Lines kept verbatim as an HTML block when no supported block owns them.</summary>
    private Converted ConvertRawLines(int firstLine, int lastLine, ImmutableArray<ChainEntry> chain)
    {
        var (text, prefixes) = ReadLines(firstLine, lastLine, chain);
        return new Converted(new HtmlBlock { Id = _ids.NextId(), Text = text, LinePrefixes = prefixes }, firstLine, lastLine);
    }

    /// <summary>
    /// Joins the bodies of consecutive lines with "\n" and collects their prefixes. Lines from
    /// <paramref name="lazyFrom"/> on may be lazy continuations (paragraph text).
    /// </summary>
    private (string Text, ImmutableArray<LinePrefix> Prefixes) ReadLines(int firstLine, int lastLine, ImmutableArray<ChainEntry> chain, int lazyFrom = int.MaxValue)
    {
        var bodies = new List<string>(lastLine - firstLine + 1);
        var prefixes = new List<LinePrefix>(lastLine - firstLine + 1);
        for (int line = firstLine; line <= lastLine; line++)
        {
            var scan = LineScanner.Scan(_source, line, chain, allowLazy: line >= lazyFrom);
            bodies.Add(BodyText(line, scan));
            prefixes.Add(scan.Prefix);
        }

        return (string.Join('\n', bodies), CompactPrefixes(prefixes));
    }

    private string BodyText(int line, in LineScan scan) => _source.Slice(scan.Body.Position, _source.LineEnd(line));

    private string RestOfLine(int line, in LineCursor cursor) => _source.Slice(cursor.Position, _source.LineEnd(line));

    /// <summary>An empty array when no line recorded any prefix, so top-level blocks store nothing.</summary>
    internal static ImmutableArray<LinePrefix> CompactPrefixes(IReadOnlyList<LinePrefix> prefixes)
    {
        foreach (var prefix in prefixes)
        {
            if (!prefix.IsNone)
            {
                return [.. prefixes];
            }
        }

        return [];
    }

    private IEnumerable<Md.Block> OrderedChildren(Md.ContainerBlock container)
    {
        IEnumerable<Md.Block> children = container.Where(static child => child is not (Md.LinkReferenceDefinitionGroup or Md.BlankLineBlock));
        if (_rehomed.TryGetValue(container, out var rehomed))
        {
            children = children.Concat(rehomed);
        }

        return children.OrderBy(MdFirstLine);
    }

    /// <summary>
    /// Without trivia tracking Markdig collects every link reference definition into one group at
    /// document level. Put each back into the innermost container enclosing its first line.
    /// </summary>
    private void RehomeDefinitions(Md.MarkdownDocument markdown)
    {
        var definitions = Md.MarkdownObjectExtensions.Descendants<Md.LinkReferenceDefinitionGroup>(markdown)
            .SelectMany(static group => group.OfType<Md.LinkReferenceDefinition>())
            .ToList();
        foreach (var definition in definitions)
        {
            var host = FindHost(markdown, definition.Line);
            if (!_rehomed.TryGetValue(host, out var list))
            {
                list = [];
                _rehomed.Add(host, list);
            }

            list.Add(definition);
        }

        // Last lines computed while re-homing may not include definitions moved later.
        _lastLines.Clear();
    }

    private Md.ContainerBlock FindHost(Md.ContainerBlock container, int line)
    {
        foreach (var child in container)
        {
            if (child is Md.ContainerBlock inner
                and not (Md.LinkReferenceDefinitionGroup or MdTables.Table)
                && MdFirstLine(inner) <= line && line <= MdLastLine(inner))
            {
                return FindHost(inner, line);
            }
        }

        return container;
    }

    /// <summary>The first physical line of a Markdig block.</summary>
    private static int MdFirstLine(Md.Block block)
    {
        return block switch
        {
            // Paragraphs keep a stale start after Markdig extracts leading link reference definitions;
            // a Setext heading's Line is its underline.
            Md.ParagraphBlock { Lines.Count: > 0 } paragraph => paragraph.Lines.Lines[0].Line,
            Md.HeadingBlock { IsSetext: true, Lines.Count: > 0 } heading => heading.Lines.Lines[0].Line,
            _ => block.Line,
        };
    }

    /// <summary>The last physical line of a Markdig block, as far as Markdig knows it.</summary>
    private int MdLastLine(Md.Block block)
    {
        if (_lastLines.TryGetValue(block, out int cached))
        {
            return cached;
        }

        int last = block.Line;
        if (block.Span.End >= block.Span.Start && block.Span.End >= 0)
        {
            last = Math.Max(last, _source.LineOf(block.Span.End));
        }

        if (block is Md.LeafBlock { Lines.Count: > 0 } leaf)
        {
            last = Math.Max(last, leaf.Lines.Lines[leaf.Lines.Count - 1].Line);
        }

        if (block is Md.ContainerBlock container)
        {
            foreach (var child in container)
            {
                last = Math.Max(last, MdLastLine(child));
            }
        }

        if (block is Md.ContainerBlock owner && _rehomed.TryGetValue(owner, out var rehomed))
        {
            foreach (var child in rehomed)
            {
                last = Math.Max(last, MdLastLine(child));
            }
        }

        _lastLines[block] = last;
        return last;
    }
}
