using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.Markdown.Parsing;
using Md = Markdig.Syntax;
using MdTables = Markdig.Extensions.Tables;

namespace Quill.Editor.Markdown.Import;

internal sealed partial class BlockConverter
{
    private Converted ConvertQuote(Md.QuoteBlock quote, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var quoteChain = chain.Add(new ChainEntry(id, ChainKind.BlockQuote));
        var children = ConvertChildren(quote, quoteChain);
        int first = quote.Line;
        int last = children.Count > 0 ? Math.Max(first, children.Max(static child => child.LastLine)) : first;

        // Trailing blank lines that still carry this quote's marker belong to the quote.
        int spanEnd = quote.Span.End >= 0 ? _source.LineOf(quote.Span.End) : first;
        for (int line = last + 1; line <= spanEnd; line++)
        {
            var scan = LineScanner.Scan(_source, line, quoteChain);
            if (scan.LazyFrom <= chain.Length || !scan.Body.RestIsBlank())
            {
                break;
            }

            last = line;
        }

        var body = ConvertBody(children, first, last, quoteChain, previousIsContent: false, BodyKind.Blocks);
        return new Converted(new BlockQuote { Id = id, Children = body }, first, last);
    }

    private Converted ConvertList(Md.ListBlock list, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var items = new List<Converted>();
        foreach (var item in list.OfType<Md.ListItemBlock>())
        {
            items.Add(ConvertListItem(item, list.IsOrdered, chain));
        }

        int first = items[0].FirstLine;
        int last = items[^1].LastLine;
        var children = ConvertBody(items, first, last, chain, previousIsContent: false, BodyKind.ListItems);
        var block = new ListBlock
        {
            Id = id,
            IsOrdered = list.IsOrdered,
            Children = children,
        };
        bool loose = list.IsLoose || ListRules.HasHeadlessItem(block);
        block = block with { Syntax = new ListSyntax(list.IsOrdered ? list.OrderedDelimiter : list.BulletType, loose) };
        return new Converted(block, first, last);
    }

    private Converted ConvertListItem(Md.ListItemBlock item, bool ordered, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var markdownChildren = OrderedChildren(item).ToList();
        bool opensBlank = item.ColumnWidth < 0 && markdownChildren.Count > 0 && MdFirstLine(markdownChildren[0]) > item.Line;
        var (itemChain, markerScan) = ItemChain(id, item.Line, chain, opensBlank);

        var children = new List<Converted>();
        foreach (var child in markdownChildren)
        {
            children.AddRange(ConvertBlock(child, itemChain));
        }

        children.Sort((left, right) => left.FirstLine.CompareTo(right.FirstLine));
        int last = IndentedEmptyLinesEnd(children.Count > 0 ? Math.Max(item.Line, children.Max(static child => child.LastLine)) : item.Line, itemChain);
        if (children.Count == 0 && last > item.Line)
        {
            // Only empty paragraphs follow an empty marker line: the item opens with a blank line, as
            // when other blocks follow (Q53).
            opensBlank = true;
            (itemChain, markerScan) = ItemChain(id, item.Line, chain, opensBlank);
        }
        else if (children.Count == 0)
        {
            // An empty item: the rest of the marker line is an empty paragraph.
            var empty = new Paragraph
            {
                Id = _ids.NextId(),
                Text = BodyText(item.Line, markerScan),
                LinePrefixes = CompactPrefixes([markerScan.Prefix]),
            };
            children.Add(new Converted(empty, item.Line, item.Line));
        }

        // A task marker opens the item's text: on the marker line, or on the next line when the marker
        // line is empty. After a blank line the item's text is the empty paragraph before (Q53).
        string? taskMarker = null;
        if (children.Count > 0 && children[0].Block is Paragraph first && IsTaskMarker(first.Text)
            && (!opensBlank || children[0].FirstLine == item.Line + 1))
        {
            taskMarker = first.Text[..4];
            children[0] = children[0] with { Block = first with { Text = first.Text[4..] } };
        }

        var (indent, marker, spacing) = SplitOpening(markerScan.OpeningAt(chain.Length));
        int contentFirst = opensBlank ? item.Line + 1 : item.Line;
        var body = ConvertBody(children, contentFirst, last, itemChain, previousIsContent: false, BodyKind.ItemBlocks, firstIsContent: taskMarker is not null);
        var block = new ListItem
        {
            Id = id,
            Children = body,
            Number = ordered ? ParseNumber(marker) : null,
            Task = taskMarker is null ? TaskState.None : taskMarker[1] == ' ' ? TaskState.Unchecked : TaskState.Checked,
            Syntax = new ListItemSyntax
            {
                Indent = indent,
                Marker = marker,
                Spacing = spacing,
                OpensWithBlankLine = opensBlank,
                OpeningLinePrefix = opensBlank ? markerScan.Prefix : LinePrefix.None,
                TaskMarker = taskMarker,
            },
        };
        return new Converted(block, item.Line, last);
    }

    /// <summary>
    /// The chain for an item's lines. The content width is measured on the marker line itself rather
    /// than taken from Markdig's ColumnWidth, whose base column differs inside extension containers
    /// such as footnotes.
    /// </summary>
    private (ImmutableArray<ChainEntry> Chain, LineScan MarkerScan) ItemChain(BlockId id, int markerLine, ImmutableArray<ChainEntry> chain, bool opensBlank)
    {
        var opening = new ChainEntry(id, ChainKind.ListItem, markerLine, ContentWidth: 0, opensBlank);
        var markerScan = LineScanner.Scan(_source, markerLine, chain.Add(opening));
        return (chain.Add(opening with { ContentWidth = markerScan.OpeningWidthAt(chain.Length) }), markerScan);
    }

    /// <summary>
    /// The last line of an item: that of its last block, or of the empty paragraphs after it written
    /// as Typora writes them at the end of an item (Q56): each a blank line indented to the item's
    /// content, after a blank line that separates. Markdig leaves these lines out of the item.
    /// </summary>
    private int IndentedEmptyLinesEnd(int last, ImmutableArray<ChainEntry> itemChain)
    {
        int end = last;
        while (end + 2 < _source.LineCount && IsBlankInItem(end + 1, itemChain, out _)
            && IsBlankInItem(end + 2, itemChain, out bool reachesContent) && reachesContent)
        {
            end += 2;
        }

        return end;
    }

    private bool IsBlankInItem(int line, ImmutableArray<ChainEntry> itemChain, out bool reachesContent)
    {
        var scan = LineScanner.Scan(_source, line, itemChain);
        reachesContent = scan.ReachesItemContent;
        return scan.LazyFrom == itemChain.Length && scan.Body.RestIsBlank();
    }

    /// <summary>GFM task list item marker at the start of the item's first paragraph: "[ ]", "[x]" or "[X]" and a space or tab.</summary>
    private static bool IsTaskMarker(string text)
    {
        return text.Length >= 4
            && text[0] == '['
            && (text[1] is ' ' or 'x' or 'X')
            && text[2] == ']'
            && (text[3] is ' ' or '\t');
    }

    /// <summary>Splits a list item opening into indentation, marker and the spacing after it.</summary>
    private static (string Indent, string Marker, string Spacing) SplitOpening(string opening)
    {
        int markerStart = 0;
        while (markerStart < opening.Length && opening[markerStart] is ' ' or '\t')
        {
            markerStart++;
        }

        int markerEnd = markerStart;
        while (markerEnd < opening.Length && char.IsAsciiDigit(opening[markerEnd]))
        {
            markerEnd++;
        }

        if (markerEnd < opening.Length)
        {
            markerEnd++; // The bullet, or the delimiter after the digits.
        }

        return (opening[..markerStart], opening[markerStart..markerEnd], opening[markerEnd..]);
    }

    private static int? ParseNumber(string marker)
    {
        var digits = marker.AsSpan().TrimEnd(".)");
        return int.TryParse(digits, out int number) ? number : null;
    }

    private Converted ConvertTable(MdTables.Table table, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        var alignments = table.ColumnDefinitions
            .Select(static definition => definition.Alignment switch
            {
                MdTables.TableColumnAlign.Left => TableAlignment.Left,
                MdTables.TableColumnAlign.Center => TableAlignment.Center,
                MdTables.TableColumnAlign.Right => TableAlignment.Right,
                _ => TableAlignment.None,
            })
            .ToImmutableArray();

        var rows = ImmutableList.CreateBuilder<Block>();
        int lastLine = table.Line;
        foreach (var row in table.OfType<MdTables.TableRow>())
        {
            rows.Add(ConvertTableRow(row, chain));
            lastLine = Math.Max(lastLine, row.Line);
        }

        int delimiterLine = table.Line + 1;
        var delimiterScan = LineScanner.Scan(_source, delimiterLine, chain);
        var block = new Table
        {
            Id = id,
            Alignments = alignments,
            Children = rows.ToImmutable(),
            Syntax = new TableSyntax
            {
                DelimiterRow = new SyntaxLine(BodyText(delimiterLine, delimiterScan), delimiterScan.Prefix),
                Alignments = alignments,
            },
        };
        return new Converted(block, table.Line, Math.Max(lastLine, delimiterLine));
    }

    private TableRow ConvertTableRow(MdTables.TableRow row, ImmutableArray<ChainEntry> chain)
    {
        int line = row.Line;
        var scan = LineScanner.Scan(_source, line, chain);
        int lineEnd = _source.LineEnd(line);
        int cursor = scan.Body.Position;

        var cells = ImmutableList.CreateBuilder<Block>();
        var written = ImmutableArray.CreateBuilder<BlockId>();
        var gaps = ImmutableArray.CreateBuilder<string>();
        foreach (var cell in row.OfType<MdTables.TableCell>())
        {
            var cellId = _ids.NextId();
            int start = cell.Span.Start;
            bool padded = start >= lineEnd || start < cursor;
            if (padded)
            {
                // A cell added by the parser to fill the row; it has no text in the source.
                cells.Add(new TableCell { Id = cellId, Text = string.Empty });
                continue;
            }

            int end = Math.Max(start, cell.Span.End + 1);
            gaps.Add(_source.Slice(cursor, start));
            cells.Add(new TableCell { Id = cellId, Text = _source.Slice(start, end) });
            written.Add(cellId);
            cursor = end;
        }

        gaps.Add(_source.Slice(cursor, lineEnd));
        return new TableRow
        {
            Id = _ids.NextId(),
            Children = cells.ToImmutable(),
            Syntax = new TableRowSyntax { Prefix = scan.Prefix, Gaps = gaps.ToImmutable(), Cells = written.ToImmutable() },
        };
    }

    private Converted ConvertLinkDefinition(Md.LinkReferenceDefinition definition, ImmutableArray<ChainEntry> chain)
    {
        // Markdig may leave the span of a title it rejected (such as '"title" ok'); without a title the
        // definition ends with its destination.
        bool hasTitle = definition.Title is not null && definition.TitleSpan.End > definition.TitleSpan.Start;
        int first = definition.Line;
        int last = Math.Max(first, _source.LineOf(hasTitle ? definition.TitleSpan.End : definition.UrlSpan.End));
        var joined = new JoinedLines(_source, first, last, chain);

        int labelStart = joined.Map(definition.LabelSpan.Start);
        int labelEnd = joined.Map(definition.LabelSpan.End) + 1;
        int destinationStart = joined.Map(definition.UrlSpan.Start);
        int destinationEnd = joined.Map(definition.UrlSpan.End) + 1;
        string text = joined.Text;
        if (destinationEnd - destinationStart >= 2 && text[destinationStart] == '<' && text[destinationEnd - 1] == '>')
        {
            destinationStart++;
            destinationEnd--;
        }

        int titleStart = hasTitle ? joined.Map(definition.TitleSpan.Start) + 1 : text.Length;
        int titleEnd = hasTitle ? joined.Map(definition.TitleSpan.End) : text.Length;

        int[] bounds = [0, labelStart, labelEnd, destinationStart, destinationEnd, titleStart, titleEnd, text.Length];
        var parts = joined.Split(bounds);
        var syntax = new LinkReferenceDefinitionSyntax
        {
            Opening = parts[0].ToSyntaxText(),
            AfterLabel = parts[2].ToSyntaxText(),
            AfterDestination = parts[4].ToSyntaxText(),
            AfterTitle = parts[6].ToSyntaxText(),
            HasTitle = hasTitle,
        };
        var block = new LinkReferenceDefinition
        {
            Id = _ids.NextId(),
            Children =
            [
                parts[1].ToSlot(_ids.NextId(), DefinitionSlotKind.LinkLabel),
                parts[3].ToSlot(_ids.NextId(), DefinitionSlotKind.LinkDestination),
                parts[5].ToSlot(_ids.NextId(), DefinitionSlotKind.LinkTitle),
            ],
            Syntax = syntax,
        };
        return new Converted(block, first, last);
    }

    private Converted ConvertFootnote(FootnoteDefinitionBlock footnote, ImmutableArray<ChainEntry> chain)
    {
        var id = _ids.NextId();
        int definitionLine = footnote.Line;
        var footnoteChain = chain.Add(new ChainEntry(id, ChainKind.Footnote, definitionLine));
        var scan = LineScanner.Scan(_source, definitionLine, footnoteChain);
        int bodyStart = scan.Body.Position;
        int lineEnd = _source.LineEnd(definitionLine);
        string text = _source.Text;

        // "[^" + name + "]:" + whitespace + content
        int caret = footnote.LabelSpan.Start;
        int nameEnd = footnote.LabelSpan.End + 1;
        var markdownChildren = OrderedChildren(footnote).ToList();
        bool contentOnDefinitionLine = markdownChildren.Count > 0 && MdFirstLine(markdownChildren[0]) == definitionLine;
        bool wellFormed = caret >= bodyStart && text[caret] == '^'
            && nameEnd + 1 < lineEnd && text[nameEnd] == ']' && text[nameEnd + 1] == ':';
        if (!wellFormed || (contentOnDefinitionLine && markdownChildren[0] is not Md.ParagraphBlock))
        {
            int lastLine = MdLastLine(footnote);
            _diagnostics.Add($"Footnote definition on lines {definitionLine}-{lastLine} has an unsupported shape; kept as raw text.");
            return ConvertRawLines(definitionLine, lastLine, chain);
        }

        int contentStart = nameEnd + 2;
        while (contentStart < lineEnd && text[contentStart] is ' ' or '\t')
        {
            contentStart++;
        }

        var continuation = new List<Converted>();
        string content = string.Empty;
        if (contentOnDefinitionLine)
        {
            var paragraph = (Md.ParagraphBlock)markdownChildren[0];
            content = _source.Slice(contentStart, lineEnd);
            var (_, paragraphLast) = LeafLines(paragraph);
            if (paragraphLast > definitionLine)
            {
                // A footnote's content is a single line (Q30); later lines of its first paragraph become
                // a paragraph of their own that keeps the footnote's indentation.
                var (rest, prefixes) = ReadLines(definitionLine + 1, paragraphLast, footnoteChain, lazyFrom: definitionLine + 1);
                continuation.Add(new Converted(new Paragraph { Id = _ids.NextId(), Text = rest, LinePrefixes = prefixes }, definitionLine + 1, paragraphLast));
            }

            markdownChildren.RemoveAt(0);
        }
        else
        {
            contentStart = lineEnd;
        }

        foreach (var child in markdownChildren)
        {
            continuation.AddRange(ConvertBlock(child, footnoteChain));
        }

        continuation.Sort((left, right) => left.FirstLine.CompareTo(right.FirstLine));
        int last = continuation.Count > 0 ? Math.Max(definitionLine, continuation.Max(static child => child.LastLine)) : definitionLine;
        var body = ConvertBody(continuation, definitionLine + 1, last, footnoteChain, previousIsContent: true, BodyKind.Blocks);
        var block = new FootnoteDefinition
        {
            Id = id,
            Children = ImmutableList.Create<Block>(
                    new DefinitionSlot { Id = _ids.NextId(), SlotKind = DefinitionSlotKind.FootnoteName, Text = _source.Slice(caret + 1, nameEnd) },
                    new DefinitionSlot { Id = _ids.NextId(), SlotKind = DefinitionSlotKind.FootnoteContent, Text = content })
                .AddRange(body),
            Syntax = new FootnoteDefinitionSyntax
            {
                Prefix = scan.Prefix,
                Opening = _source.Slice(bodyStart, caret + 1),
                Separator = _source.Slice(nameEnd, contentStart),
            },
        };
        return new Converted(block, definitionLine, last);
    }
}
