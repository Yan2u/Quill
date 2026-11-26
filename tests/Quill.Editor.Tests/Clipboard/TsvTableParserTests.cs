using Quill.Editor.Editing;

namespace Quill.Editor.Tests.Clipboard;

public sealed class TsvTableParserTests
{
    [Fact]
    public void ParsesBasicSpreadsheetTsvWithTrailingNewline()
    {
        const string tsv = "Step\tR_mean\tR_std\r\n1\t0.6073\t0.2509\r\n2\t0.6444\t0.2396\r\n";
        var grid = TsvTableParser.TryParse(tsv, insideTable: false);

        Assert.NotNull(grid);
        Assert.Equal(3, grid!.RowCount);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(["Step", "R_mean", "R_std"], grid.Rows[0]);
        Assert.Equal(["1", "0.6073", "0.2509"], grid.Rows[1]);
        Assert.Equal(["2", "0.6444", "0.2396"], grid.Rows[2]);
    }

    [Fact]
    public void ParsesRfc4180QuotedMultilineTabAndEscapedQuotes()
    {
        const string tsv = "Col1\tCol2\n\"Line1\nLine2\tKeep\"\t\"He said \"\"hi\"\"\"\n";
        var grid = TsvTableParser.TryParse(tsv, insideTable: false);

        Assert.NotNull(grid);
        Assert.Equal(2, grid!.RowCount);
        Assert.Equal("Line1<br>Line2\tKeep", grid.Rows[1][0]);
        Assert.Equal("He said \"hi\"", grid.Rows[1][1]);
    }

    [Fact]
    public void EscapesPipesInTsvCells()
    {
        const string tsv = "A|B\tC\n1\t2|3";
        var grid = TsvTableParser.TryParse(tsv, insideTable: false);
        Assert.NotNull(grid);
        Assert.Equal("A\\|B", grid!.Rows[0][0]);
        Assert.Equal("2\\|3", grid.Rows[1][1]);
    }

    [Fact]
    public void SingleRowTsvRejectedOutsideTableButAcceptedInsideTable()
    {
        const string oneRow = "a\tb\tc";
        Assert.Null(TsvTableParser.TryParse(oneRow, insideTable: false));

        var inside = TsvTableParser.TryParse(oneRow, insideTable: true);
        Assert.NotNull(inside);
        Assert.Equal(1, inside!.RowCount);
        Assert.Equal(3, inside.ColumnCount);
        Assert.Equal(["a", "b", "c"], inside.Rows[0]);
    }

    [Fact]
    public void PlainMultilineTextWithoutTabsRejectedInBothModes()
    {
        const string noTabs = "x|y\nz";
        Assert.Null(TsvTableParser.TryParse(noTabs, insideTable: false));
        Assert.Null(TsvTableParser.TryParse(noTabs, insideTable: true));
    }

    [Theory]
    [InlineData("\tint a = 1;\n\tint b = 2;")]
    [InlineData("a\tb\nc\td\te")]
    [InlineData("a,b,c\n1,2,3")]
    [InlineData("```\na\tb\nc\td\n```")]
    [InlineData("| a\t1 | b |\n| --- | --- |\n| c | d |")]
    public void RejectsCodeRaggedProseCsvAndMarkdownBlocksOutsideTable(string text)
    {
        Assert.Null(TsvTableParser.TryParse(text, insideTable: false));
    }

    [Fact]
    public void InsideTablePadsRaggedRows()
    {
        const string ragged = "a\tb\nc\td\te";
        var grid = TsvTableParser.TryParse(ragged, insideTable: true);
        Assert.NotNull(grid);
        Assert.Equal(2, grid!.RowCount);
        Assert.Equal(3, grid.ColumnCount);
        Assert.Equal(["a", "b", ""], grid.Rows[0]);
        Assert.Equal(["c", "d", "e"], grid.Rows[1]);
    }
}
