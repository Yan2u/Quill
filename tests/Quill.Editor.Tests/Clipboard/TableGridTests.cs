using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Clipboard;

public sealed class TableGridTests
{
    [Fact]
    public void CreatePadsRaggedRowsAndNormalizesCellsAndAlignments()
    {
        var grid = TableGrid.Create(
            [
                ["A|B", "C\r\nD"],
                ["OnlyOne"],
            ],
            [TableAlignment.Left],
            hasHeader: true);

        Assert.Equal(2, grid.RowCount);
        Assert.Equal(2, grid.ColumnCount);
        Assert.True(grid.HasHeader);
        Assert.Equal(["A\\|B", "C<br>D"], grid.Rows[0]);
        Assert.Equal(["OnlyOne", ""], grid.Rows[1]);
        Assert.Equal([TableAlignment.Left, TableAlignment.None], grid.Alignments);
    }

    [Fact]
    public void ToTableProducesValidDocumentAndRoundTripsThroughSerializer()
    {
        var ids = new SequentialBlockIdSource();
        var grid = TableGrid.Create(
            [
                ["Name", "Value"],
                ["x\\|y", "1\n2"],
            ],
            [TableAlignment.Left, TableAlignment.Right],
            hasHeader: true);

        var table = grid.ToTable(ids);
        var doc = new Document { Id = ids.NextId(), Children = [table] };

        Assert.Empty(DocumentValidator.Validate(doc));
        string markdown = MarkdownSerializer.SerializeFragment([table]);
        Assert.Equal("| Name | Value |\n| :--- | ---: |\n| x\\|y | 1<br>2 |", markdown);

        var reimported = MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
        var parsedTable = Assert.IsType<Table>(Assert.Single(reimported.Children));
        Assert.Equal(grid.Alignments, parsedTable.Alignments);
        Assert.Equal("1<br>2", ((TableCell)((TableRow)parsedTable.Children[1]).Children[1]).Text);
    }

    [Fact]
    public void FromTableCapturesRowsAndBodyOnlyFlag()
    {
        var doc = MarkdownImporter.Import("| h1 | h2 |\n| :-: | - |\n| v1 | v2 |", new SequentialBlockIdSource()).Document;
        var table = Assert.IsType<Table>(Assert.Single(doc.Children));

        var grid = TableGrid.FromTable(table, isBodyOnly: true);
        Assert.True(grid.IsBodyOnly);
        Assert.False(grid.HasHeader);
        Assert.Equal([TableAlignment.Center, TableAlignment.None], grid.Alignments);
        Assert.Equal(["h1", "h2"], grid.Rows[0]);
        Assert.Equal(["v1", "v2"], grid.Rows[1]);
    }
}
