using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Syntax generated for blocks created by editing (no syntax metadata): one blank line between new
/// blocks (06-markdown-io.md), and output that imports back to the same structure.
/// </summary>
public sealed class GenerationTests
{
    private readonly ModelBuilder _build = new();

    [Fact]
    public void NewBlocksAreSeparatedByOneBlankLine()
    {
        var document = _build.Document(
            _build.Heading(2, "Title"),
            _build.Paragraph("line one\nline two"),
            _build.Rule(),
            _build.Toc(),
            _build.Math("x^2"),
            _build.Html("<div>raw</div>"));
        Assert.Equal("## Title\n\nline one\nline two\n\n---\n\n[toc]\n\n$$\nx^2\n$$\n\n<div>raw</div>\n", SerializeStable(document));
    }

    [Fact]
    public void CodeFenceIsLongerThanAnyFenceInTheCode()
    {
        var document = _build.Document(_build.Code("```\ninner\n```", "md"));
        Assert.Equal("````md\n```\ninner\n```\n````\n", SerializeStable(document));
    }

    [Fact]
    public void QuoteWithNestedBlocks()
    {
        var document = _build.Document(_build.Quote(
            _build.Paragraph("a"),
            _build.Paragraph(string.Empty),
            _build.Bullets(_build.Item("b")),
            _build.Quote(_build.Paragraph("c"))));
        Assert.Equal("> a\n>\n>\n> - b\n>\n> > c\n", SerializeStable(document));
    }

    [Fact]
    public void Table()
    {
        var document = _build.Document(_build.Table(
            [TableAlignment.None, TableAlignment.Left, TableAlignment.Center, TableAlignment.Right],
            ["a", "b", "c", "d"],
            ["1", "2 \\| 3", "", "4"]));
        Assert.Equal("| a | b | c | d |\n| --- | :--- | :---: | ---: |\n| 1 | 2 \\| 3 |  | 4 |\n", SerializeStable(document));
    }

    [Fact]
    public void TableRowWithNewTextInAPaddedCellIsRewritten()
    {
        var document = Import("| a | b |\n|---|---|\n| 1 |\n");
        var table = (Table)document.Children[0];
        var row = (TableRow)table.Children[1];
        var cell = (TableCell)row.Children[1];
        var changed = document with
        {
            Children = [table with { Children = table.Children.SetItem(1, row with { Children = row.Children.SetItem(1, cell with { Text = "2" }) }) }],
        };
        Assert.Equal("| a | b |\n|---|---|\n| 1 | 2 |\n", Serialize(changed));
    }

    [Fact]
    public void Definitions()
    {
        var document = _build.Document(
            _build.Paragraph("Text[^1] and [link][a]."),
            _build.Footnote("1", "The note.", _build.Paragraph("More of the note.")),
            _build.LinkDefinition("a", "https://example.com", "Title"),
            _build.LinkDefinition("b", "my file.md"));
        Assert.Equal(
            "Text[^1] and [link][a].\n\n[^1]: The note.\n\n    More of the note.\n\n[a]: https://example.com \"Title\"\n\n[b]: <my file.md>\n",
            SerializeStable(document));
    }

    [Fact]
    public void ChangedHeadingLevelRegeneratesTheMarkers()
    {
        var document = Import("  ## Title ##\n\nSub\n===\n");
        var atx = (Heading)document.Children[0];
        var setext = (Heading)document.Children[1];
        var changed = document with { Children = [atx with { Level = 3 }, setext with { Level = 2 }] };
        Assert.Equal("  ### Title\n\nSub\n---\n", Serialize(changed));

        var deeper = document with { Children = [atx, setext with { Level = 4 }] };
        Assert.Equal("  ## Title ##\n\n#### Sub\n", Serialize(deeper));
    }

    [Fact]
    public void ChangedCodeInfoRegeneratesTheOpeningFenceOnly()
    {
        var document = Import("~~~ js\ncode\n~~~\n");
        var code = (CodeFence)document.Children[0];
        var language = code.Language with { Text = "ts" };
        Assert.Equal("~~~ts\ncode\n~~~\n", Serialize(document with { Children = [code with { Children = [code.Code, language] }] }));
    }

    [Fact]
    public void EditedLinesOfAContainerGetGeneratedPrefixes()
    {
        // Only new physical lines get generated prefixes; the lazy line keeps having none.
        var document = Import(">  a\nb\n");
        var quote = (BlockQuote)document.Children[0];
        var paragraph = (Paragraph)quote.Children[0];
        var edited = paragraph with { Text = paragraph.Text + "\nc", LinePrefixes = paragraph.LinePrefixes.Add(default) };
        var changed = document with { Children = [quote with { Children = ImmutableList.Create<Block>(edited) }] };
        Assert.Equal(">  a\nb\n> c\n", Serialize(changed));
    }
}
