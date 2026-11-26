using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Clipboard;

public sealed class ClipboardHtmlWriterTests
{
    [Fact]
    public void CopyTableWritesCleanSemanticCfHtmlWithExcelLineBreakStyleAndResolvedLinks()
    {
        const string dsl = """
            | **Name** | Value | Link |
            | :--- | ---: | :---: |
            | a<br>b | `x` &copy; | [ref][r] |

            [r]: https://example.com
            """;

        var host = EditorTestHost.Load(dsl.Replace("| **Name**", "| <|>Name"));
        var copied = host.Session.CopyTable();

        Assert.NotNull(copied);
        Assert.NotNull(copied!.Html);
        Assert.Contains("mso-data-placement:same-cell", copied.Html);

        var decoded = CfHtmlCodec.Decode(copied.Html!);
        Assert.Contains("<thead>", decoded.Fragment);
        Assert.Contains("<tbody>", decoded.Fragment);
        Assert.Contains("align=\"left\"", decoded.Fragment);
        Assert.Contains("align=\"right\"", decoded.Fragment);
        Assert.Contains("align=\"center\"", decoded.Fragment);
        Assert.Contains("<br style=\"mso-data-placement:same-cell;\">", decoded.Fragment);
        Assert.Contains("<code>x</code>", decoded.Fragment);
        Assert.Contains("<a href=\"https://example.com\">ref</a>", decoded.Fragment);

        var extracted = HtmlTableExtractor.Extract(copied.Html!);
        Assert.NotNull(extracted);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(extracted!.Segments)).Grid;
        Assert.False(grid.IsBodyOnly);
        Assert.Equal([TableAlignment.Left, TableAlignment.Right, TableAlignment.Center], grid.Alignments);
        Assert.Equal(["a<br>b", "`x` ©", "[ref](https://example.com)"], grid.Rows[1]);
    }

    [Fact]
    public void CtrlCOnBodyRowOrSingleRowMultiCellMarksBodySliceWithoutSyntheticHeader()
    {
        var host = EditorTestHost.Load("| h1 | h2 | h3 |\n| - | - | - |\n| a | **b<|>** | c |\n| d | e | f |");
        host.Press("Ctrl+L Ctrl+C");

        Assert.NotNull(host.Clipboard?.Html);
        var decoded = CfHtmlCodec.Decode(host.Clipboard!.Html!);
        Assert.Contains("data-quill-table-slice=\"body\"", decoded.Fragment);
        Assert.DoesNotContain("<thead>", decoded.Fragment);
        Assert.DoesNotContain("h1", decoded.Fragment);
        Assert.Contains("<strong>b</strong>", decoded.Fragment);

        var extracted = HtmlTableExtractor.Extract(host.Clipboard.Html!);
        var grid = Assert.IsType<HtmlTableSegment>(Assert.Single(extracted!.Segments)).Grid;
        Assert.True(grid.IsBodyOnly);
        Assert.Equal(1, grid.RowCount);
        Assert.Equal(["a", "**b**", "c"], grid.Rows[0]);
    }

    [Fact]
    public void SameCellSelectionCopyDoesNotWriteTableHtml()
    {
        var host = EditorTestHost.Load("| <^>hello<$> | world |\n| - | - |");
        host.Press("Ctrl+C");
        Assert.Null(host.Clipboard!.Html);
    }

    [Fact]
    public void CopyAsMarkdownAndCopyAsPlainTextDoNotWriteHtml()
    {
        var host = EditorTestHost.Load("| a<|> | b |\n| - | - |\n| c | d |");
        host.Press("Ctrl+A Ctrl+Shift+C");
        Assert.Null(host.Clipboard!.Html);

        host.Run(ClipboardAction.CopyAsPlainText);
        Assert.Null(host.Clipboard!.Html);
    }

    [Fact]
    public void MultiBlockSelectionWithTableWritesSemanticHtml()
    {
        var host = EditorTestHost.Load("<^>Intro\n\n| h1 | h2 |\n| - | - |\n| 1 | 2<$> |");
        host.Press("Ctrl+C");
        Assert.NotNull(host.Clipboard!.Html);
        var decoded = CfHtmlCodec.Decode(host.Clipboard.Html!);
        Assert.Contains("<p>Intro</p>", decoded.Fragment);
        Assert.Contains("<table>", decoded.Fragment);
    }
}
