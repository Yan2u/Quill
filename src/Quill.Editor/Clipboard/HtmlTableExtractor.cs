using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Quill.Editor.Documents;

namespace Quill.Editor.Editing;

internal abstract record HtmlPasteSegment;

internal sealed record HtmlTableSegment(TableGrid Grid) : HtmlPasteSegment;

internal sealed record HtmlTextSegment(string Markdown) : HtmlPasteSegment;

internal sealed record HtmlExtractionResult(IReadOnlyList<HtmlPasteSegment> Segments, bool HasTable);

/// <summary>
/// Extracts structured <see cref="TableGrid"/>s (and surrounding Markdown text segments for mixed
/// "text + table" fragments) from Windows CF_HTML clipboard payloads produced by Excel, Word,
/// Word Online, WPS, Google Docs/Sheets, browsers, and Quill itself.
/// </summary>
internal static partial class HtmlTableExtractor
{
    private const int MaxColSpan = 256;
    private const int MaxRowSpan = 1000;

    private static readonly HashSet<string> AriaCellRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "cell", "gridcell", "columnheader", "rowheader",
    };

    private static readonly HashSet<string> BlockTags = new(StringComparer.Ordinal)
    {
        "p", "div", "h1", "h2", "h3", "h4", "h5", "h6",
        "ul", "ol", "li", "blockquote", "pre", "section", "article", "header", "footer",
    };

    private readonly record struct CssProperties(
        TableAlignment? Align,
        bool? Bold,
        bool? Italic,
        bool? Strike,
        bool? Underline);

    private readonly record struct InlineContext(
        bool Bold = false,
        bool Italic = false,
        bool Strike = false,
        bool Code = false,
        bool PreserveWhitespace = false);

    private sealed class StyleSheet
    {
        private readonly Dictionary<string, CssProperties> _rules = new(StringComparer.OrdinalIgnoreCase);

        public static StyleSheet Parse(string? css)
        {
            var sheet = new StyleSheet();
            if (string.IsNullOrWhiteSpace(css))
            {
                return sheet;
            }

            foreach (Match match in CssRuleRegex().Matches(css))
            {
                string selectorList = match.Groups[1].Value.Trim();
                if (selectorList.StartsWith('@'))
                {
                    continue;
                }

                var props = ParseStyleDeclarations(match.Groups[2].Value);
                if (props == default)
                {
                    continue;
                }

                foreach (string rawSelector in selectorList.Split(','))
                {
                    string selector = rawSelector.Trim();
                    if (selector.Length == 0 || selector.Contains(' ') || selector.Contains('>') || selector.Contains(':'))
                    {
                        continue;
                    }

                    sheet._rules[selector] = sheet._rules.TryGetValue(selector, out var existing)
                        ? Merge(existing, props)
                        : props;
                }
            }

            return sheet;
        }

        public CssProperties Resolve(HtmlElement element)
        {
            var resolved = default(CssProperties);
            if (_rules.TryGetValue(element.Tag, out var tagRule))
            {
                resolved = Merge(resolved, tagRule);
            }

            if (element.GetAttribute("class") is { Length: > 0 } classAttr)
            {
                foreach (string cls in classAttr.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (_rules.TryGetValue("." + cls, out var classRule))
                    {
                        resolved = Merge(resolved, classRule);
                    }

                    if (_rules.TryGetValue(element.Tag + "." + cls, out var tagClassRule))
                    {
                        resolved = Merge(resolved, tagClassRule);
                    }
                }
            }

            if (element.GetAttribute("style") is { Length: > 0 } inlineStyle)
            {
                resolved = Merge(resolved, ParseStyleDeclarations(inlineStyle));
            }

            if (resolved.Align is null && element.GetAttribute("align") is { Length: > 0 } alignAttr)
            {
                resolved = resolved with { Align = ParseAlignment(alignAttr) };
            }

            return resolved;
        }

        private static CssProperties Merge(CssProperties baseProps, CssProperties overrideProps) => new(
            overrideProps.Align ?? baseProps.Align,
            overrideProps.Bold ?? baseProps.Bold,
            overrideProps.Italic ?? baseProps.Italic,
            overrideProps.Strike ?? baseProps.Strike,
            overrideProps.Underline ?? baseProps.Underline);
    }

    public static HtmlExtractionResult? Extract(string rawCfHtml)
    {
        if (string.IsNullOrWhiteSpace(rawCfHtml))
        {
            return null;
        }

        var payload = CfHtmlCodec.Decode(rawCfHtml);
        if (string.IsNullOrWhiteSpace(payload.Fragment))
        {
            return null;
        }

        string htmlToParse = WrapFragmentIfNeeded(payload);
        var root = HtmlFragmentParser.Parse(htmlToParse);
        if (!ContainsSemanticTable(root))
        {
            return null;
        }

        var styles = StyleSheet.Parse(payload.StylesCss);
        var segments = new List<HtmlPasteSegment>();
        var pendingBlocks = new List<string>();
        CollectSegments(root, styles, segments, pendingBlocks);
        FlushPendingText(segments, pendingBlocks);

        if (segments.Count == 0)
        {
            return null;
        }

        bool hasTable = segments.Any(static segment => segment is HtmlTableSegment);
        return new HtmlExtractionResult(segments, hasTable);
    }

    private static string WrapFragmentIfNeeded(CfHtmlPayload payload)
    {
        string trimmed = payload.Fragment.TrimStart();
        if (trimmed.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
        {
            return payload.Fragment;
        }

        if (payload.FragmentInsideRow && !trimmed.StartsWith("<tr", StringComparison.OrdinalIgnoreCase))
        {
            return $"<table><tbody><tr>{payload.Fragment}</tr></tbody></table>";
        }

        if (payload.FragmentInsideTable)
        {
            return $"<table>{payload.Fragment}</table>";
        }

        return payload.Fragment;
    }

    private static void CollectSegments(
        HtmlElement element,
        StyleSheet styles,
        List<HtmlPasteSegment> segments,
        List<string> pendingBlocks)
    {
        if (IsSemanticTable(element))
        {
            var grid = ExtractGrid(element, styles);
            if (!grid.IsEmpty)
            {
                if (grid.IsSingleCell)
                {
                    string singleText = ExtractSingleCellAsText(element, styles);
                    if (singleText.Length > 0)
                    {
                        pendingBlocks.Add(singleText);
                    }
                }
                else
                {
                    FlushPendingText(segments, pendingBlocks);
                    segments.Add(new HtmlTableSegment(grid));
                }
            }

            return;
        }

        if (!ContainsSemanticTable(element))
        {
            CollectTopLevelTextBlocks(element, styles, pendingBlocks);
            return;
        }

        var inlineRuns = new List<HtmlNode>();

        void FlushInlineRuns()
        {
            if (inlineRuns.Count == 0)
            {
                return;
            }

            string text = RenderInlineList(inlineRuns, styles, new InlineContext());
            text = CollapseAndTrimParagraph(text);
            if (text.Length > 0)
            {
                pendingBlocks.Add(text);
            }

            inlineRuns.Clear();
        }

        foreach (var child in element.Children)
        {
            if (child is HtmlElement childElement && (ContainsSemanticTable(childElement) || IsBlockTag(childElement.Tag)))
            {
                FlushInlineRuns();
                CollectSegments(childElement, styles, segments, pendingBlocks);
            }
            else
            {
                inlineRuns.Add(child);
            }
        }

        FlushInlineRuns();
    }

    private static void FlushPendingText(List<HtmlPasteSegment> segments, List<string> pendingBlocks)
    {
        if (pendingBlocks.Count == 0)
        {
            return;
        }

        string joined = string.Join("\n\n", pendingBlocks);
        if (joined.Length > 0)
        {
            segments.Add(new HtmlTextSegment(joined));
        }

        pendingBlocks.Clear();
    }

    private static void CollectTopLevelTextBlocks(HtmlElement element, StyleSheet styles, List<string> output)
    {
        switch (element.Tag)
        {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
            {
                int level = element.Tag[1] - '0';
                string text = CollapseAndTrimParagraph(RenderInlineList(element.Children, styles, new InlineContext())).Replace('\n', ' ');
                if (text.Length > 0)
                {
                    output.Add(new string('#', level) + " " + text);
                }

                return;
            }

            case "pre":
            {
                string raw = CollectRawText(element).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
                if (raw.Length > 0)
                {
                    output.Add("```\n" + raw + "\n```");
                }

                return;
            }

            case "ul" or "ol":
            {
                var lines = new List<string>();
                int index = 1;
                foreach (var child in element.Children.OfType<HtmlElement>())
                {
                    if (child.Tag == "li")
                    {
                        string itemText = CollapseAndTrimParagraph(RenderInlineList(child.Children, styles, new InlineContext())).Replace("\n", " ");
                        string prefix = element.Tag == "ol" ? $"{index++}. " : "- ";
                        lines.Add(prefix + itemText);
                    }
                }

                if (lines.Count > 0)
                {
                    output.Add(string.Join('\n', lines));
                }

                return;
            }

            case "blockquote":
            {
                var inner = new List<string>();
                foreach (var child in element.Children)
                {
                    if (child is HtmlElement el)
                    {
                        CollectTopLevelTextBlocks(el, styles, inner);
                    }
                    else if (child is HtmlText txt)
                    {
                        string collapsed = CollapseHtmlWhitespace(txt.Value).Trim();
                        if (collapsed.Length > 0)
                        {
                            inner.Add(collapsed);
                        }
                    }
                }

                if (inner.Count > 0)
                {
                    string quoted = string.Join("\n>\n", inner.Select(block => string.Join('\n', block.Split('\n').Select(l => "> " + l))));
                    output.Add(quoted);
                }

                return;
            }

            case "hr":
                output.Add("---");
                return;
        }

        if (element.Children.OfType<HtmlElement>().Any(static child => IsBlockTag(child.Tag) || child.Tag == "hr"))
        {
            var inlineBuffer = new List<HtmlNode>();

            void FlushInlineBuffer()
            {
                if (inlineBuffer.Count == 0)
                {
                    return;
                }

                string text = CollapseAndTrimParagraph(RenderInlineList(inlineBuffer, styles, new InlineContext()));
                if (text.Length > 0)
                {
                    output.Add(text);
                }

                inlineBuffer.Clear();
            }

            foreach (var child in element.Children)
            {
                if (child is HtmlElement childEl && (IsBlockTag(childEl.Tag) || childEl.Tag == "hr"))
                {
                    FlushInlineBuffer();
                    CollectTopLevelTextBlocks(childEl, styles, output);
                }
                else
                {
                    inlineBuffer.Add(child);
                }
            }

            FlushInlineBuffer();
            return;
        }

        string content = CollapseAndTrimParagraph(RenderInlineList(element.Children, styles, new InlineContext()));
        if (content.Length > 0)
        {
            output.Add(content);
        }
    }

    private static TableGrid ExtractGrid(HtmlElement tableElement, StyleSheet styles)
    {
        bool isBodyOnly = string.Equals(tableElement.GetAttribute("data-quill-table-slice"), "body", StringComparison.OrdinalIgnoreCase);
        var collectedRows = CollectTableRows(tableElement);
        if (collectedRows.Count == 0)
        {
            return TableGrid.Empty;
        }

        var gridRows = new List<List<string?>>();
        var columnHeaderAlignments = new Dictionary<int, TableAlignment>();
        var columnBodyAlignments = new Dictionary<int, TableAlignment?>();
        bool hasHeader = false;
        int nextRow = 0;

        for (int rowIndex = 0; rowIndex < collectedRows.Count; rowIndex++)
        {
            var (rowEl, inThead) = collectedRows[rowIndex];
            var cellElements = CollectRowCells(rowEl);
            if (cellElements.Count == 0)
            {
                continue;
            }

            int r = nextRow++;
            EnsureRowExists(gridRows, r);
            int c = 0;

            bool rowAllTh = true;
            foreach (var cellEl in cellElements)
            {
                while (c < gridRows[r].Count && gridRows[r][c] is not null)
                {
                    c++;
                }

                bool isTh = cellEl.Tag == "th" || string.Equals(cellEl.GetAttribute("role"), "columnheader", StringComparison.OrdinalIgnoreCase);
                rowAllTh &= isTh;

                int colspan = ParseSpan(cellEl.GetAttribute("colspan"), defaultVal: 1, max: MaxColSpan);
                int rowspan = ParseSpan(cellEl.GetAttribute("rowspan"), defaultVal: 1, max: MaxRowSpan, zeroMeansSectionEnd: true, remainingRows: collectedRows.Count - rowIndex);

                string cellMarkdown = RenderCellContent(cellEl, styles);
                var cellCss = styles.Resolve(cellEl);
                TableAlignment? alignment = cellCss.Align;
                if (alignment is null && cellEl.Children.Count == 1 && cellEl.Children[0] is HtmlElement singleP && singleP.Tag is "p" or "div")
                {
                    alignment = styles.Resolve(singleP).Align;
                }

                for (int dr = 0; dr < rowspan; dr++)
                {
                    EnsureRowExists(gridRows, r + dr);
                    while (gridRows[r + dr].Count < c + colspan)
                    {
                        gridRows[r + dr].Add(null);
                    }

                    for (int dc = 0; dc < colspan; dc++)
                    {
                        gridRows[r + dr][c + dc] = dr == 0 && dc == 0 ? cellMarkdown : string.Empty;
                    }
                }

                if (colspan == 1 && alignment is { } explicitAlign and not TableAlignment.None)
                {
                    if (inThead || r == 0)
                    {
                        columnHeaderAlignments.TryAdd(c, explicitAlign);
                    }
                    else if (!columnBodyAlignments.TryGetValue(c, out var current))
                    {
                        columnBodyAlignments[c] = explicitAlign;
                    }
                    else if (current != explicitAlign)
                    {
                        columnBodyAlignments[c] = TableAlignment.None;
                    }
                }

                c += colspan;
            }

            if (inThead || (r == 0 && rowAllTh))
            {
                hasHeader = true;
            }
        }

        if (gridRows.Count == 0)
        {
            return TableGrid.Empty;
        }

        int maxCols = gridRows.Max(static row => row.Count);
        if (maxCols == 0)
        {
            return TableGrid.Empty;
        }

        var finalRows = new List<IReadOnlyList<string>>(gridRows.Count);
        foreach (var row in gridRows)
        {
            var normalizedRow = new string[maxCols];
            for (int c = 0; c < maxCols; c++)
            {
                normalizedRow[c] = c < row.Count ? row[c] ?? string.Empty : string.Empty;
            }

            finalRows.Add(normalizedRow);
        }

        var alignments = new TableAlignment[maxCols];
        for (int c = 0; c < maxCols; c++)
        {
            if (columnBodyAlignments.TryGetValue(c, out var bodyAlign))
            {
                alignments[c] = bodyAlign ?? TableAlignment.None;
            }
            else if (columnHeaderAlignments.TryGetValue(c, out var headAlign))
            {
                alignments[c] = headAlign;
            }
            else
            {
                alignments[c] = TableAlignment.None;
            }
        }

        return TableGrid.Create(finalRows, alignments, hasHeader: hasHeader && !isBodyOnly, isBodyOnly: isBodyOnly);
    }

    private static string ExtractSingleCellAsText(HtmlElement tableElement, StyleSheet styles)
    {
        var rows = CollectTableRows(tableElement);
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var cells = CollectRowCells(rows[0].Row);
        if (cells.Count == 0)
        {
            return string.Empty;
        }

        return RenderCellCore(cells[0], styles, escapePipes: false, lineSeparator: "\n");
    }

    private static List<(HtmlElement Row, bool InThead)> CollectTableRows(HtmlElement table)
    {
        if (table.Tag != "table")
        {
            var ariaRows = new List<(HtmlElement, bool)>();
            CollectAriaRows(table, ariaRows);
            return ariaRows;
        }

        var theadRows = new List<(HtmlElement, bool)>();
        var bodyRows = new List<(HtmlElement, bool)>();
        var tfootRows = new List<(HtmlElement, bool)>();

        foreach (var child in table.Children.OfType<HtmlElement>())
        {
            switch (child.Tag)
            {
                case "thead":
                    CollectDirectRows(child, inThead: true, theadRows);
                    break;
                case "tbody":
                    CollectDirectRows(child, inThead: false, bodyRows);
                    break;
                case "tfoot":
                    CollectDirectRows(child, inThead: false, tfootRows);
                    break;
                case "tr":
                    bodyRows.Add((child, false));
                    break;
            }
        }

        var result = new List<(HtmlElement, bool)>(theadRows.Count + bodyRows.Count + tfootRows.Count);
        result.AddRange(theadRows);
        result.AddRange(bodyRows);
        result.AddRange(tfootRows);
        return result;
    }

    private static void CollectDirectRows(HtmlElement section, bool inThead, List<(HtmlElement, bool)> output)
    {
        foreach (var child in section.Children.OfType<HtmlElement>())
        {
            if (child.Tag == "tr")
            {
                output.Add((child, inThead));
            }
        }
    }

    private static void CollectAriaRows(HtmlElement container, List<(HtmlElement, bool)> output)
    {
        foreach (var child in container.Children.OfType<HtmlElement>())
        {
            if (child != container && IsSemanticTable(child))
            {
                continue;
            }

            string? role = child.GetAttribute("role");
            if (string.Equals(role, "row", StringComparison.OrdinalIgnoreCase))
            {
                output.Add((child, false));
            }
            else
            {
                CollectAriaRows(child, output);
            }
        }
    }

    private static List<HtmlElement> CollectRowCells(HtmlElement row)
    {
        if (row.Tag == "tr")
        {
            return row.Children.OfType<HtmlElement>().Where(static child => child.Tag is "td" or "th").ToList();
        }

        var cells = new List<HtmlElement>();
        foreach (var child in row.Children.OfType<HtmlElement>())
        {
            if (child.GetAttribute("role") is { } role && AriaCellRoles.Contains(role))
            {
                cells.Add(child);
            }
        }

        return cells;
    }

    private static string RenderCellContent(HtmlElement cell, StyleSheet styles) =>
        RenderCellCore(cell, styles, escapePipes: true, lineSeparator: "<br>");

    private static string RenderCellCore(HtmlElement cell, StyleSheet styles, bool escapePipes, string lineSeparator)
    {
        var cellCss = styles.Resolve(cell);
        var baseCtx = new InlineContext(
            Bold: cellCss.Bold == true,
            Italic: cellCss.Italic == true,
            Strike: cellCss.Strike == true);
        string raw = RenderCellBlocks(cell.Children, styles, baseCtx);
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        string[] parts = raw.Split('\n');
        var cleaned = new List<string>(parts.Length);
        foreach (string part in parts)
        {
            string line = part.Replace('\u00A0', ' ').Trim();
            cleaned.Add(line);
        }

        int first = 0;
        while (first < cleaned.Count && cleaned[first].Length == 0)
        {
            first++;
        }

        int last = cleaned.Count - 1;
        while (last >= first && cleaned[last].Length == 0)
        {
            last--;
        }

        if (first > last)
        {
            return string.Empty;
        }

        var lines = cleaned.Skip(first).Take(last - first + 1).Select(line =>
        {
            if (line.Length == 0)
            {
                return line;
            }

            string formatted = line;
            if (cellCss.Underline == true)
            {
                formatted = $"<u>{formatted}</u>";
            }

            if (cellCss.Strike == true)
            {
                formatted = $"~~{formatted}~~";
            }

            if (cellCss.Italic == true)
            {
                formatted = $"*{formatted}*";
            }

            if (cellCss.Bold == true)
            {
                formatted = $"**{formatted}**";
            }

            return formatted;
        });

        string joined = string.Join('\n', lines);
        return escapePipes ? TableGrid.NormalizeCellText(joined) : joined.Replace("\n", lineSeparator, StringComparison.Ordinal);
    }

    private static string RenderCellBlocks(IReadOnlyList<HtmlNode> nodes, StyleSheet styles, InlineContext ctx)
    {
        var blocks = new List<string>();
        var inlineNodes = new List<HtmlNode>();

        void FlushInlines()
        {
            if (inlineNodes.Count == 0)
            {
                return;
            }

            string text = RenderInlineList(inlineNodes, styles, ctx);
            if (!string.IsNullOrWhiteSpace(text.Replace('\u00A0', ' ')))
            {
                blocks.Add(text);
            }
            else if (text.Contains('\n'))
            {
                blocks.Add(text);
            }

            inlineNodes.Clear();
        }

        foreach (var node in nodes)
        {
            if (node is HtmlElement element)
            {
                if (IsSemanticTable(element))
                {
                    FlushInlines();
                    string flattened = FlattenNestedTable(element, styles);
                    if (flattened.Length > 0)
                    {
                        blocks.Add(flattened);
                    }

                    continue;
                }

                if (element.Tag is "p" or "div" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "blockquote")
                {
                    FlushInlines();
                    string nested = RenderCellBlocks(element.Children, styles, ApplyElementCss(element, styles, ctx));
                    if (!string.IsNullOrWhiteSpace(nested.Replace('\u00A0', ' ')))
                    {
                        blocks.Add(nested);
                    }

                    continue;
                }

                if (element.Tag is "ul" or "ol")
                {
                    FlushInlines();
                    int itemNo = 1;
                    foreach (var li in element.Children.OfType<HtmlElement>().Where(static c => c.Tag == "li"))
                    {
                        string liText = RenderCellBlocks(li.Children, styles, ctx).Trim();
                        string bullet = element.Tag == "ol" ? $"{itemNo++}. " : "• ";
                        blocks.Add(bullet + liText);
                    }

                    continue;
                }

                if (element.Tag == "pre")
                {
                    FlushInlines();
                    string preText = CollectRawText(element).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim('\n');
                    if (preText.Length > 0)
                    {
                        blocks.Add(preText);
                    }

                    continue;
                }
            }

            inlineNodes.Add(node);
        }

        FlushInlines();
        return string.Join('\n', blocks);
    }

    private static string FlattenNestedTable(HtmlElement nestedTable, StyleSheet styles)
    {
        var nestedGrid = ExtractGrid(nestedTable, styles);
        if (nestedGrid.IsEmpty)
        {
            return string.Empty;
        }

        var rowStrings = new List<string>(nestedGrid.RowCount);
        foreach (var row in nestedGrid.Rows)
        {
            string line = string.Join(" ", row.Where(static cell => cell.Length > 0));
            if (line.Length > 0)
            {
                rowStrings.Add(line);
            }
        }

        return string.Join('\n', rowStrings);
    }

    private static string RenderInlineList(IReadOnlyList<HtmlNode> nodes, StyleSheet styles, InlineContext ctx)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.Append(RenderInlineNode(node, styles, ctx));
        }

        return builder.ToString();
    }

    private static string RenderInlineNode(HtmlNode node, StyleSheet styles, InlineContext ctx)
    {
        if (node is HtmlText text)
        {
            return ctx.PreserveWhitespace ? text.Value : CollapseHtmlWhitespace(text.Value);
        }

        var element = (HtmlElement)node;
        switch (element.Tag)
        {
            case "br":
                return "\n";
            case "img":
                return RenderImage(element);
            case "a":
                return RenderLink(element, styles, ctx);
            case "code" or "tt":
            {
                string inner = RenderInlineList(element.Children, styles, ctx with { Code = true, PreserveWhitespace = true });
                return FormatCodeSpan(inner);
            }
            case "sub":
                return WrapTag("sub", RenderInlineList(element.Children, styles, ctx));
            case "sup":
                return WrapTag("sup", RenderInlineList(element.Children, styles, ctx));
            case "kbd":
                return WrapTag("kbd", RenderInlineList(element.Children, styles, ctx));
        }

        if (IsSemanticTable(element))
        {
            return FlattenNestedTable(element, styles);
        }

        var css = styles.Resolve(element);
        bool wantBold = !ctx.Bold && (css.Bold == true || (element.Tag is "b" or "strong" && css.Bold != false));
        bool wantItalic = !ctx.Italic && (css.Italic == true || (element.Tag is "i" or "em" && css.Italic != false));
        bool wantStrike = !ctx.Strike && (css.Strike == true || (element.Tag is "del" or "s" or "strike" && css.Strike != false));
        bool wantUnderline = css.Underline == true || (element.Tag == "u" && css.Underline != false);

        var nextCtx = ctx with
        {
            Bold = ctx.Bold || wantBold,
            Italic = ctx.Italic || wantItalic,
            Strike = ctx.Strike || wantStrike,
        };

        string content = RenderInlineList(element.Children, styles, nextCtx);
        if (content.Length == 0)
        {
            return string.Empty;
        }

        if (wantUnderline && element.Tag != "a")
        {
            content = WrapWithEdgeWhitespace(content, "<u>", "</u>");
        }

        if (wantStrike)
        {
            content = WrapWithEdgeWhitespace(content, "~~", "~~");
        }

        if (wantItalic)
        {
            content = WrapWithEdgeWhitespace(content, "*", "*");
        }

        if (wantBold)
        {
            content = WrapWithEdgeWhitespace(content, "**", "**");
        }

        return content;
    }

    private static InlineContext ApplyElementCss(HtmlElement element, StyleSheet styles, InlineContext ctx)
    {
        var css = styles.Resolve(element);
        return ctx with
        {
            Bold = ctx.Bold || css.Bold == true,
            Italic = ctx.Italic || css.Italic == true,
            Strike = ctx.Strike || css.Strike == true,
        };
    }

    private static string RenderLink(HtmlElement element, StyleSheet styles, InlineContext ctx)
    {
        string text = RenderInlineList(element.Children, styles, ctx).Trim();
        string? href = element.GetAttribute("href")?.Trim();
        if (string.IsNullOrEmpty(href) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return text;
        }

        if (text.Length == 0)
        {
            text = href;
        }

        string destination = href.Any(char.IsWhiteSpace) ? $"<{href}>" : href;
        return $"[{text}]({destination})";
    }

    private static string RenderImage(HtmlElement element)
    {
        string alt = element.GetAttribute("alt")?.Trim() ?? string.Empty;
        string? src = element.GetAttribute("src")?.Trim();
        if (string.IsNullOrEmpty(src) || IsTransientOfficeClipUrl(src))
        {
            return alt;
        }

        string destination = src.Any(char.IsWhiteSpace) ? $"<{src}>" : src;
        return $"![{alt}]({destination})";
    }

    private static bool IsTransientOfficeClipUrl(string url) =>
        url.StartsWith("file:///", StringComparison.OrdinalIgnoreCase)
        && (url.Contains("msohtmlclip", StringComparison.OrdinalIgnoreCase)
            || url.Contains("Content.MSO", StringComparison.OrdinalIgnoreCase));

    private static string WrapTag(string tag, string content) =>
        string.IsNullOrEmpty(content) ? string.Empty : WrapWithEdgeWhitespace(content, $"<{tag}>", $"</{tag}>");

    private static string WrapWithEdgeWhitespace(string content, string open, string close)
    {
        int start = 0;
        while (start < content.Length && content[start] is ' ' or '\n')
        {
            start++;
        }

        int end = content.Length;
        while (end > start && content[end - 1] is ' ' or '\n')
        {
            end--;
        }

        if (start >= end)
        {
            return content;
        }

        return content[..start] + open + content[start..end] + close + content[end..];
    }

    private static string FormatCodeSpan(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return string.Empty;
        }

        int longestRun = 0;
        int current = 0;
        foreach (char ch in code)
        {
            if (ch == '`')
            {
                current++;
                if (current > longestRun)
                {
                    longestRun = current;
                }
            }
            else
            {
                current = 0;
            }
        }

        string fence = new('`', longestRun + 1);
        bool needsPad = code.StartsWith('`') || code.EndsWith('`');
        return needsPad ? $"{fence} {code} {fence}" : $"{fence}{code}{fence}";
    }

    private static string CollectRawText(HtmlElement element)
    {
        var builder = new StringBuilder();
        foreach (var child in element.Children)
        {
            if (child is HtmlText text)
            {
                builder.Append(text.Value);
            }
            else if (child is HtmlElement el)
            {
                if (el.Tag == "br")
                {
                    builder.Append('\n');
                }
                else
                {
                    builder.Append(CollectRawText(el));
                }
            }
        }

        return builder.ToString();
    }

    private static string CollapseHtmlWhitespace(string text)
    {
        if (text.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        bool prevSpace = false;
        foreach (char ch in text)
        {
            if (ch is ' ' or '\t' or '\r' or '\n')
            {
                if (!prevSpace)
                {
                    builder.Append(' ');
                    prevSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                prevSpace = false;
            }
        }

        return builder.ToString();
    }

    private static string CollapseAndTrimParagraph(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        string[] lines = text.Split('\n');
        var trimmed = new List<string>(lines.Length);
        foreach (string line in lines)
        {
            string clean = line.Replace('\u00A0', ' ').Trim();
            if (clean.Length > 0)
            {
                trimmed.Add(clean);
            }
        }

        return string.Join('\n', trimmed);
    }

    private static bool ContainsSemanticTable(HtmlElement element)
    {
        if (IsSemanticTable(element))
        {
            return true;
        }

        foreach (var child in element.Children)
        {
            if (child is HtmlElement el && ContainsSemanticTable(el))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSemanticTable(HtmlElement element)
    {
        string? role = element.GetAttribute("role");
        if (string.Equals(role, "presentation", StringComparison.OrdinalIgnoreCase)
            || string.Equals(role, "none", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (element.Tag == "table")
        {
            return true;
        }

        return (string.Equals(role, "table", StringComparison.OrdinalIgnoreCase)
                || string.Equals(role, "grid", StringComparison.OrdinalIgnoreCase))
            && HasAriaRowDescendant(element);
    }

    private static bool HasAriaRowDescendant(HtmlElement element)
    {
        foreach (var child in element.Children.OfType<HtmlElement>())
        {
            if (string.Equals(child.GetAttribute("role"), "row", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (HasAriaRowDescendant(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockTag(string tag) => BlockTags.Contains(tag);

    private static void EnsureRowExists(List<List<string?>> rows, int rowIndex)
    {
        while (rows.Count <= rowIndex)
        {
            rows.Add([]);
        }
    }

    private static int ParseSpan(string? raw, int defaultVal, int max, bool zeroMeansSectionEnd = false, int remainingRows = 1)
    {
        if (string.IsNullOrWhiteSpace(raw) || !int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            return defaultVal;
        }

        if (parsed == 0 && zeroMeansSectionEnd)
        {
            return Math.Clamp(remainingRows, 1, max);
        }

        return Math.Clamp(parsed, 1, max);
    }

    private static CssProperties ParseStyleDeclarations(string declarations)
    {
        TableAlignment? align = null;
        bool? bold = null;
        bool? italic = null;
        bool? strike = null;
        bool? underline = null;

        foreach (string decl in declarations.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            int colon = decl.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            string prop = decl[..colon].Trim().ToLowerInvariant();
            string val = decl[(colon + 1)..].Trim();
            int important = val.IndexOf("!important", StringComparison.OrdinalIgnoreCase);
            if (important >= 0)
            {
                val = val[..important].Trim();
            }

            switch (prop)
            {
                case "text-align":
                    align = ParseAlignment(val) ?? align;
                    break;
                case "font-weight":
                    if (val.Equals("normal", StringComparison.OrdinalIgnoreCase) || val.Equals("lighter", StringComparison.OrdinalIgnoreCase))
                    {
                        bold = false;
                    }
                    else if (val.Equals("bold", StringComparison.OrdinalIgnoreCase) || val.Equals("bolder", StringComparison.OrdinalIgnoreCase))
                    {
                        bold = true;
                    }
                    else if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int weight))
                    {
                        bold = weight >= 600;
                    }

                    break;
                case "font-style":
                    if (val.Equals("italic", StringComparison.OrdinalIgnoreCase) || val.Equals("oblique", StringComparison.OrdinalIgnoreCase))
                    {
                        italic = true;
                    }
                    else if (val.Equals("normal", StringComparison.OrdinalIgnoreCase))
                    {
                        italic = false;
                    }

                    break;
                case "text-decoration" or "text-decoration-line":
                    if (val.Contains("line-through", StringComparison.OrdinalIgnoreCase))
                    {
                        strike = true;
                    }

                    if (val.Contains("underline", StringComparison.OrdinalIgnoreCase))
                    {
                        underline = true;
                    }

                    if (val.Equals("none", StringComparison.OrdinalIgnoreCase))
                    {
                        strike = false;
                        underline = false;
                    }

                    break;
            }
        }

        return new CssProperties(align, bold, italic, strike, underline);
    }

    private static TableAlignment? ParseAlignment(string value) => value.Trim().ToLowerInvariant() switch
    {
        "left" or "start" => TableAlignment.Left,
        "center" => TableAlignment.Center,
        "right" or "end" => TableAlignment.Right,
        _ => null,
    };

    [GeneratedRegex(@"([^{}]+)\{([^{}]*)\}")]
    private static partial Regex CssRuleRegex();
}
