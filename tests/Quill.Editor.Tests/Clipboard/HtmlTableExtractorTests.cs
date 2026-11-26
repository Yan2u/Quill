using Quill.Editor.Documents;
using Quill.Editor.Editing;

namespace Quill.Editor.Tests.Clipboard;

public sealed class HtmlTableExtractorTests
{
    [Fact]
    public void ExtractsExcelFragmentWithClassStylesAndNumericColumns()
    {
        // Matches the 7-column numeric table layout from Excel/Word Online + Excel orphan <tr> CF_HTML.
        const string html = """
            <html>
            <head>
            <style>
            <!--
            td {text-align:general; font-weight:400;}
            .xl65 {font-weight:700; text-align:center;}
            .xl66 {text-align:right;}
            br {mso-data-placement:same-cell;}
            -->
            </style>
            </head>
            <body>
            <table>
            <!--StartFragment-->
              <col width="64" span="7">
              <tr>
                <td class="xl65">Step</td><td class="xl65">R_mean</td><td class="xl65">R_std</td>
                <td class="xl65">SlowR</td><td class="xl65">FastR</td><td class="xl65">IF</td><td class="xl65">TT</td>
              </tr>
              <tr>
                <td class="xl66">1</td><td class="xl66">0.6073</td><td class="xl66">0.2509</td>
                <td class="xl66">0.696</td><td class="xl66">0.5187</td><td class="xl66">3.13</td><td class="xl66">3.84</td>
              </tr>
              <tr>
                <td class="xl66">2</td><td class="xl66">0.6444</td><td class="xl66">0.2396</td>
                <td class="xl66">0.7534</td><td class="xl66">0.5354</td><td class="xl66">3.02</td><td class="xl66">3.76</td>
              </tr>
            <!--EndFragment-->
            </table>
            </body>
            </html>
            """;

        var result = HtmlTableExtractor.Extract(html);
        Assert.NotNull(result);
        Assert.True(result!.HasTable);
        var tableSeg = Assert.IsType<HtmlTableSegment>(Assert.Single(result.Segments));
        var grid = tableSeg.Grid;

        Assert.Equal(3, grid.RowCount);
        Assert.Equal(7, grid.ColumnCount);
        Assert.Equal(["**Step**", "**R_mean**", "**R_std**", "**SlowR**", "**FastR**", "**IF**", "**TT**"], grid.Rows[0]);
        Assert.Equal(["1", "0.6073", "0.2509", "0.696", "0.5187", "3.13", "3.84"], grid.Rows[1]);
        Assert.Equal(["2", "0.6444", "0.2396", "0.7534", "0.5354", "3.02", "3.76"], grid.Rows[2]);
        Assert.All(grid.Alignments, static align => Assert.Equal(TableAlignment.Right, align));
    }

    [Fact]
    public void ExtractsExcelMultilineCellWithBr()
    {
        const string html = "<table><!--StartFragment--><tr><td>Line 1<br>Line 2</td><td>A|B</td></tr><tr><td>3</td><td>4</td></tr><!--EndFragment--></table>";
        var result = HtmlTableExtractor.Extract(html);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;
        Assert.Equal("Line 1<br>Line 2", grid.Rows[0][0]);
        Assert.Equal("A\\|B", grid.Rows[0][1]);
    }

    [Fact]
    public void ExpandsColspanAndRowspanIntoRectangularGrid()
    {
        const string html = """
            <table>
              <tr><th colspan="2">Merged Header</th><th rowspan="2">Side</th></tr>
              <tr><td>Left</td><td>Middle</td></tr>
              <tr><td>A</td><td colspan="2">Span Bottom</td></tr>
            </table>
            """;

        var result = HtmlTableExtractor.Extract(html);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;

        Assert.True(grid.HasHeader);
        Assert.Equal(3, grid.RowCount);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(["Merged Header", "", "Side"], grid.Rows[0]);
        Assert.Equal(["Left", "Middle", ""], grid.Rows[1]);
        Assert.Equal(["A", "Span Bottom", ""], grid.Rows[2]);
    }

    [Fact]
    public void ExtractsWordAndWpsDialectWithMsoNormalAndConditionalComments()
    {
        const string wordHtml = """
            <html>
            <body>
            <!--StartFragment-->
            <table class="MsoTableGrid">
              <tr>
                <td><p class="MsoNormal">Name<!--[if supportFields]>ignored<![endif]--></p></td>
                <td><p class="MsoNormal">Notes<o:p>&nbsp;</o:p></p></td>
              </tr>
              <tr>
                <td><p class="MsoNormal">Alice
                  Smith</p></td>
                <td><p class="MsoNormal"><o:p>&nbsp;</o:p></p></td>
              </tr>
              <tr>
                <td><p class="MsoNormal">Bob</p><p class="MsoNormal">Second line</p></td>
                <td><p class="MsoNormal"><b>Bold</b> and <i>Italic</i></p></td>
              </tr>
            </table>
            <!--EndFragment-->
            </body>
            </html>
            """;

        var result = HtmlTableExtractor.Extract(wordHtml);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;

        Assert.Equal(3, grid.RowCount);
        Assert.Equal(["Name", "Notes"], grid.Rows[0]);
        Assert.Equal(["Alice Smith", ""], grid.Rows[1]);
        Assert.Equal(["Bob<br>Second line", "**Bold** and *Italic*"], grid.Rows[2]);
    }

    [Fact]
    public void HandlesGoogleDocsNormalWeightBoldWrapper()
    {
        const string docsHtml = """
            <b style="font-weight:normal;" id="docs-internal-guid-1234">
              <table>
                <tr>
                  <td><p><span style="font-weight:700;">Header</span></p></td>
                  <td><p><span style="font-style:italic;">Sub</span></p></td>
                </tr>
                <tr>
                  <td><p>Plain</p></td>
                  <td><p><span style="text-decoration:line-through;">Old</span></p></td>
                </tr>
              </table>
            </b>
            """;

        var result = HtmlTableExtractor.Extract(docsHtml);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;

        Assert.Equal(["**Header**", "*Sub*"], grid.Rows[0]);
        Assert.Equal(["Plain", "~~Old~~"], grid.Rows[1]);
    }

    [Fact]
    public void PreservesTheadBodyTfootOrderAndInlineMarkdownFormatting()
    {
        const string html = """
            <table>
              <tfoot><tr><td>Sum</td><td>100</td></tr></tfoot>
              <tbody><tr><td><u>Item</u> <code>a`b</code></td><td><del>10</del> <sub>2</sub><sup>3</sup> <kbd>Ctrl</kbd></td></tr></tbody>
              <thead><tr><th align="left"><b>Key</b></th><th align="center"><a href="https://example.com">Val</a></th></tr></thead>
            </table>
            """;

        var result = HtmlTableExtractor.Extract(html);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;

        Assert.True(grid.HasHeader);
        Assert.Equal([TableAlignment.Left, TableAlignment.Center], grid.Alignments);
        Assert.Equal(["**Key**", "[Val](https://example.com)"], grid.Rows[0]);
        Assert.Equal(["<u>Item</u> ``a`b``", "~~10~~ <sub>2</sub><sup>3</sup> <kbd>Ctrl</kbd>"], grid.Rows[1]);
        Assert.Equal(["Sum", "100"], grid.Rows[2]);
    }

    [Fact]
    public void ExtractsAriaRoleGridTable()
    {
        const string ariaHtml = """
            <div role="grid">
              <div role="row">
                <div role="columnheader">Col A</div>
                <div role="columnheader">Col B</div>
              </div>
              <div role="row">
                <div role="gridcell">1</div>
                <div role="gridcell">2</div>
              </div>
            </div>
            """;

        var result = HtmlTableExtractor.Extract(ariaHtml);
        Assert.NotNull(result);
        Assert.True(result!.HasTable);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result.Segments)).Grid;
        Assert.True(grid.HasHeader);
        Assert.Equal(["Col A", "Col B"], grid.Rows[0]);
        Assert.Equal(["1", "2"], grid.Rows[1]);
    }

    [Fact]
    public void IgnoresPresentationTableAndFlattensNestedTable()
    {
        const string presentationOnly = "<table role=\"presentation\"><tr><td>Layout</td><td>Only</td></tr></table>";
        Assert.Null(HtmlTableExtractor.Extract(presentationOnly));

        const string nested = """
            <table>
              <tr><th>Outer1</th><th>Outer2</th></tr>
              <tr><td><table><tr><td>In1</td><td>In2</td></tr><tr><td>In3</td><td>In4</td></tr></table></td><td>Right</td></tr>
            </table>
            """;

        var result = HtmlTableExtractor.Extract(nested);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(2, grid.ColumnCount);
        Assert.Equal(["In1 In2<br>In3 In4", "Right"], grid.Rows[1]);
    }

    [Fact]
    public void DowngradesSingleCellTableToTextAndAllowsSingleColumnMultiRowTable()
    {
        const string singleCell = "<table><tr><td>Line 1<br>A|B</td></tr></table>";
        var single = HtmlTableExtractor.Extract(singleCell);
        Assert.NotNull(single);
        Assert.False(single!.HasTable);
        var textSeg = Assert.IsType<HtmlTextSegment>(Assert.Single(single.Segments));
        Assert.Equal("Line 1\nA|B", textSeg.Markdown);

        const string singleCol = "<table><tr><td>Row 1</td></tr><tr><td>Row 2</td></tr></table>";
        var colResult = HtmlTableExtractor.Extract(singleCol);
        Assert.NotNull(colResult);
        Assert.True(colResult!.HasTable);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(colResult.Segments)).Grid;
        Assert.Equal(2, grid.RowCount);
        Assert.Equal(1, grid.ColumnCount);
        Assert.Equal(["Row 1"], grid.Rows[0]);
        Assert.Equal(["Row 2"], grid.Rows[1]);
    }

    [Fact]
    public void ExtractsMixedTextAndTableSegments()
    {
        const string mixed = """
            <!--StartFragment-->
            <h2>Summary</h2>
            <p>Intro <b>text</b>.</p>
            <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            <p>Trailing note</p>
            <!--EndFragment-->
            """;

        var result = HtmlTableExtractor.Extract(mixed);
        Assert.NotNull(result);
        Assert.True(result!.HasTable);
        Assert.Equal(3, result.Segments.Count);
        Assert.Equal("## Summary\n\nIntro **text**.", Assert.IsType<HtmlTextSegment>(result.Segments[0]).Markdown);
        Assert.Equal(2, Assert.IsType<HtmlTableSegment>(result.Segments[1]).Grid.RowCount);
        Assert.Equal("Trailing note", Assert.IsType<HtmlTextSegment>(result.Segments[2]).Markdown);
    }

    [Fact]
    public void PreservesBodyOnlySliceMarker()
    {
        const string html = "<table data-quill-table-slice=\"body\"><tbody><tr><td>c</td><td>d</td></tr></tbody></table>";
        var result = HtmlTableExtractor.Extract(html);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(result!.Segments)).Grid;
        Assert.True(grid.IsBodyOnly);
        Assert.False(grid.HasHeader);
        Assert.Equal(["c", "d"], grid.Rows[0]);
    }

    [Fact]
    public void ReturnsNullWhenHtmlHasNoTable()
    {
        const string html = "<p>Hello <b>world</b></p>";
        Assert.Null(HtmlTableExtractor.Extract(html));
    }
}
