using System.Net;
using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Serializes Quill tables and table-containing selections to clean semantic HTML wrapped in
/// Windows CF_HTML for interoperability with Excel, Word, WPS, Google Sheets, and web editors (Q120).
/// </summary>
internal static class ClipboardHtmlWriter
{
    private const string ExcelSameCellBrStyle = "mso-data-placement:same-cell;";

    /// <summary>Serializes a complete table for "Copy table" (Q77, Q120).</summary>
    public static string WriteTable(Document document, Table table)
    {
        var references = ReferenceIndex.Of(document);
        var html = new StringBuilder();
        AppendTable(html, table, startRow: 0, startCol: 0, endRow: table.Children.Count - 1, endCol: table.ColumnCount - 1, startOffset: null, endOffset: null, bodyOnlySlice: false, document, references);
        return CfHtmlCodec.Encode(html.ToString(), includeExcelBrStyle: true);
    }

    /// <summary>
    /// Serializes a selection to CF_HTML when it spans multiple cells or contains at least one table;
    /// returns null for same-cell or non-table selections (Q120).
    /// </summary>
    public static string? WriteSelection(Document document, Position start, Position end, IReadOnlyList<Block> slicedBlocks)
    {
        if (start is TextPosition s && end is TextPosition e && s.Block == e.Block)
        {
            return null;
        }

        if (TryWriteSingleTableSelection(document, start, end, out string? tableHtml))
        {
            return CfHtmlCodec.Encode(tableHtml!, includeExcelBrStyle: true);
        }

        if (!ContainsTable(slicedBlocks))
        {
            return null;
        }

        var references = ReferenceIndex.Of(document);
        var html = new StringBuilder();
        foreach (var block in slicedBlocks)
        {
            AppendBlock(html, block, document, references);
        }

        return html.Length > 0 ? CfHtmlCodec.Encode(html.ToString(), includeExcelBrStyle: true) : null;
    }

    private static bool TryWriteSingleTableSelection(Document document, Position start, Position end, out string? html)
    {
        html = null;
        if (start is not TextPosition s || end is not TextPosition e
            || DocumentTree.TryFind(document, s.Block) is not TableCell
            || DocumentTree.TryFind(document, e.Block) is not TableCell)
        {
            return false;
        }

        var startLocation = DocumentTree.Locate(document, s.Block);
        var endLocation = DocumentTree.Locate(document, e.Block);
        if (startLocation.Ancestors.Length < 2 || endLocation.Ancestors.Length < 2)
        {
            return false;
        }

        var (startTable, startRow) = startLocation.Ancestors[^2];
        var (endTable, endRow) = endLocation.Ancestors[^2];
        if (startTable.Id != endTable.Id || startTable is not Table table)
        {
            return false;
        }

        int startCol = startLocation.Index;
        int endCol = endLocation.Index;
        bool wholeHeaderOnly = startRow == 0 && endRow == 0 && startCol == 0 && endCol == table.ColumnCount - 1 && table.Children.Count == 1;
        bool bodyOnlySlice = startRow > 0 || (startRow == 0 && endRow == 0 && !wholeHeaderOnly);

        var builder = new StringBuilder();
        AppendTable(builder, table, startRow, startCol, endRow, endCol, s.Offset, e.Offset, bodyOnlySlice, document, ReferenceIndex.Of(document));
        html = builder.ToString();
        return true;
    }

    private static void AppendTable(
        StringBuilder html,
        Table table,
        int startRow,
        int startCol,
        int endRow,
        int endCol,
        int? startOffset,
        int? endOffset,
        bool bodyOnlySlice,
        Document document,
        ReferenceIndex references)
    {
        int minCol = startRow == endRow ? startCol : 0;
        int maxCol = startRow == endRow ? endCol : table.ColumnCount - 1;

        html.Append(bodyOnlySlice ? "<table data-quill-table-slice=\"body\">" : "<table>");
        bool wroteThead = false;
        bool wroteTbody = false;

        for (int r = startRow; r <= endRow && r < table.Children.Count; r++)
        {
            var row = (TableRow)table.Children[r];
            bool isHeaderRow = !bodyOnlySlice && r == 0;
            if (isHeaderRow)
            {
                html.Append("<thead>");
                wroteThead = true;
            }
            else if (!wroteTbody)
            {
                if (wroteThead)
                {
                    html.Append("</thead>");
                    wroteThead = false;
                }

                html.Append("<tbody>");
                wroteTbody = true;
            }

            html.Append("<tr>");
            for (int c = minCol; c <= maxCol && c < row.Children.Count; c++)
            {
                var cell = (TableCell)row.Children[c];
                string text;
                if (r == startRow && c < startCol)
                {
                    text = string.Empty;
                }
                else if (r == endRow && c > endCol)
                {
                    text = string.Empty;
                }
                else
                {
                    int from = r == startRow && c == startCol && startOffset is { } so ? Math.Clamp(so, 0, cell.Text.Length) : 0;
                    int to = r == endRow && c == endCol && endOffset is { } eo ? Math.Clamp(eo, from, cell.Text.Length) : cell.Text.Length;
                    text = cell.Text[from..to];
                }

                string tag = isHeaderRow ? "th" : "td";
                html.Append('<').Append(tag);
                if (c < table.Alignments.Length)
                {
                    AppendAlignmentAttributes(html, table.Alignments[c]);
                }

                html.Append('>');
                WriteInlines(html, text, cell.InlineSyntax, document, references, inTableCell: true, stripLeadingZeroWidthSpace: false);
                html.Append("</").Append(tag).Append('>');
            }

            html.Append("</tr>");
        }

        if (wroteThead)
        {
            html.Append("</thead>");
        }

        if (wroteTbody)
        {
            html.Append("</tbody>");
        }

        html.Append("</table>");
    }

    private static void AppendAlignmentAttributes(StringBuilder html, TableAlignment alignment)
    {
        switch (alignment)
        {
            case TableAlignment.Left:
                html.Append(" align=\"left\" style=\"text-align:left;\"");
                break;
            case TableAlignment.Center:
                html.Append(" align=\"center\" style=\"text-align:center;\"");
                break;
            case TableAlignment.Right:
                html.Append(" align=\"right\" style=\"text-align:right;\"");
                break;
        }
    }

    private static void AppendBlock(StringBuilder html, Block block, Document document, ReferenceIndex references)
    {
        switch (block)
        {
            case Paragraph paragraph:
                if (paragraph.IsEmpty)
                {
                    return;
                }

                html.Append("<p>");
                WriteInlines(html, paragraph.Text, paragraph.InlineSyntax, document, references, inTableCell: false, stripLeadingZeroWidthSpace: true);
                html.Append("</p>");
                break;
            case Heading heading:
                html.Append("<h").Append(heading.Level).Append('>');
                WriteInlines(html, heading.Text, heading.InlineSyntax, document, references, inTableCell: false, stripLeadingZeroWidthSpace: false);
                html.Append("</h").Append(heading.Level).Append('>');
                break;
            case Table table:
                AppendTable(html, table, 0, 0, table.Children.Count - 1, table.ColumnCount - 1, null, null, bodyOnlySlice: false, document, references);
                break;
            case BlockQuote quote:
                html.Append("<blockquote>");
                foreach (var child in quote.Children)
                {
                    AppendBlock(html, child, document, references);
                }

                html.Append("</blockquote>");
                break;
            case ListBlock list:
                string listTag = list.IsOrdered ? "ol" : "ul";
                html.Append('<').Append(listTag).Append('>');
                foreach (var item in list.Children.OfType<ListItem>())
                {
                    html.Append("<li>");
                    foreach (var child in item.Children)
                    {
                        AppendBlock(html, child, document, references);
                    }

                    html.Append("</li>");
                }

                html.Append("</").Append(listTag).Append('>');
                break;
            case CodeFence code:
                html.Append("<pre><code>").Append(WebUtility.HtmlEncode(code.Code.Text)).Append("</code></pre>");
                break;
            case MathBlock math:
                html.Append("<pre><code>").Append(WebUtility.HtmlEncode(math.Text)).Append("</code></pre>");
                break;
            case ThematicBreak:
                html.Append("<hr>");
                break;
            case HtmlBlock rawHtml:
                html.Append(rawHtml.Text);
                break;
            case FrontMatter frontMatter:
                html.Append("<pre><code>").Append(WebUtility.HtmlEncode(frontMatter.Text)).Append("</code></pre>");
                break;
            case TocBlock:
                html.Append("<p>[toc]</p>");
                break;
        }
    }

    private static void WriteInlines(
        StringBuilder html,
        string source,
        InlineSyntaxSet syntax,
        Document document,
        ReferenceIndex references,
        bool inTableCell,
        bool stripLeadingZeroWidthSpace)
    {
        if (string.IsNullOrEmpty(source))
        {
            return;
        }

        if (stripLeadingZeroWidthSpace && source[0] == Normalizer.ZeroWidthSpace)
        {
            source = source[1..];
            if (source.Length == 0)
            {
                return;
            }
        }

        var tree = MarkdownInlines.Parser.Parse(source, syntax, references.IsLinkDefined);
        foreach (var node in tree.Nodes)
        {
            WriteInlineNode(html, source, node, document, references, inTableCell);
        }
    }

    private static void WriteInlineNode(
        StringBuilder html,
        string source,
        InlineNode node,
        Document document,
        ReferenceIndex references,
        bool inTableCell)
    {
        switch (node.Kind)
        {
            case InlineKind.Text:
                html.Append(WebUtility.HtmlEncode(source[node.Range.Start..node.Range.End]));
                break;
            case InlineKind.Escape:
                html.Append(WebUtility.HtmlEncode(source[node.Content.Start..node.Content.End]));
                break;
            case InlineKind.Entity or InlineKind.Emoji:
                html.Append(WebUtility.HtmlEncode(node.Value ?? source[node.Range.Start..node.Range.End]));
                break;
            case InlineKind.Strong:
                WrapInlineChildren(html, "strong", source, node, document, references, inTableCell);
                break;
            case InlineKind.Emphasis:
                WrapInlineChildren(html, "em", source, node, document, references, inTableCell);
                break;
            case InlineKind.Strikethrough:
                WrapInlineChildren(html, "del", source, node, document, references, inTableCell);
                break;
            case InlineKind.Highlight:
                WrapInlineChildren(html, "mark", source, node, document, references, inTableCell);
                break;
            case InlineKind.Subscript:
                WrapInlineChildren(html, "sub", source, node, document, references, inTableCell);
                break;
            case InlineKind.Superscript:
                WrapInlineChildren(html, "sup", source, node, document, references, inTableCell);
                break;
            case InlineKind.Code:
                html.Append("<code>").Append(WebUtility.HtmlEncode(source[node.Content.Start..node.Content.End])).Append("</code>");
                break;
            case InlineKind.Math:
                html.Append(WebUtility.HtmlEncode(source[node.Content.Start..node.Content.End]));
                break;
            case InlineKind.Link:
            {
                string? url = ResolveDestination(node, document, references);
                if (!string.IsNullOrEmpty(url))
                {
                    html.Append("<a href=\"").Append(WebUtility.HtmlEncode(url)).Append("\">");
                    WriteInlineChildren(html, source, node, document, references, inTableCell);
                    html.Append("</a>");
                }
                else
                {
                    WriteInlineChildren(html, source, node, document, references, inTableCell);
                }

                break;
            }
            case InlineKind.Autolink or InlineKind.Url:
            {
                string display = source[node.Content.Start..node.Content.End];
                string href = node.Value ?? display;
                if (node.IsEmail && !href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    href = "mailto:" + href;
                }

                html.Append("<a href=\"").Append(WebUtility.HtmlEncode(href)).Append("\">")
                    .Append(WebUtility.HtmlEncode(display))
                    .Append("</a>");
                break;
            }
            case InlineKind.Image:
            {
                string alt = source[node.Content.Start..node.Content.End];
                string? src = ResolveDestination(node, document, references);
                if (!string.IsNullOrEmpty(src))
                {
                    html.Append("<img src=\"").Append(WebUtility.HtmlEncode(src))
                        .Append("\" alt=\"").Append(WebUtility.HtmlEncode(alt)).Append("\">");
                }
                else
                {
                    html.Append(WebUtility.HtmlEncode(alt));
                }

                break;
            }
            case InlineKind.HtmlBreak or InlineKind.HardBreak or InlineKind.SoftBreak:
                html.Append(inTableCell ? $"<br style=\"{ExcelSameCellBrStyle}\">" : "<br>");
                break;
            case InlineKind.HtmlSpan:
            {
                string tag = node.Value is "u" or "sub" or "sup" or "kbd" ? node.Value : "span";
                WrapInlineChildren(html, tag, source, node, document, references, inTableCell);
                break;
            }
            case InlineKind.FootnoteReference:
                html.Append("<sup>[").Append(WebUtility.HtmlEncode(source[node.Content.Start..node.Content.End])).Append("]</sup>");
                break;
            default:
                html.Append(WebUtility.HtmlEncode(source[node.Range.Start..node.Range.End]));
                break;
        }
    }

    private static void WrapInlineChildren(
        StringBuilder html,
        string tag,
        string source,
        InlineNode node,
        Document document,
        ReferenceIndex references,
        bool inTableCell)
    {
        html.Append('<').Append(tag).Append('>');
        WriteInlineChildren(html, source, node, document, references, inTableCell);
        html.Append("</").Append(tag).Append('>');
    }

    private static void WriteInlineChildren(
        StringBuilder html,
        string source,
        InlineNode node,
        Document document,
        ReferenceIndex references,
        bool inTableCell)
    {
        if (node.Children.IsEmpty)
        {
            html.Append(WebUtility.HtmlEncode(source[node.Content.Start..node.Content.End]));
            return;
        }

        foreach (var child in node.Children)
        {
            WriteInlineNode(html, source, child, document, references, inTableCell);
        }
    }

    private static string? ResolveDestination(InlineNode node, Document document, ReferenceIndex references)
    {
        if (node.LinkForm == LinkForm.Inline)
        {
            return string.IsNullOrEmpty(node.Value) ? null : node.Value;
        }

        if (!string.IsNullOrEmpty(node.Label)
            && references.ResolveLink(document, LinkLabel.Normalize(node.Label)) is { } defId
            && DocumentTree.TryFind(document, defId) is LinkReferenceDefinition def)
        {
            return def.Destination.Text;
        }

        return string.IsNullOrEmpty(node.Value) ? null : node.Value;
    }

    private static bool ContainsTable(IReadOnlyList<Block> blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Table)
            {
                return true;
            }

            if (block is ContainerBlock container && ContainsTable(container.Children))
            {
                return true;
            }
        }

        return false;
    }
}
