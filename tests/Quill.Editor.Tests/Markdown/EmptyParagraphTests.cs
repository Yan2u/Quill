using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.Tests.Support;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Empty paragraphs and separators (02-document-model.md, 06-markdown-io.md): between two blocks the
/// first blank line is the separator and every other blank line is an empty paragraph; blank lines at
/// the start or end of a container are all empty paragraphs; between list items they stay separators.
/// </summary>
public sealed class EmptyParagraphTests
{
    private readonly ModelBuilder _build = new();

    [Theory]
    [InlineData("a\nb\n", "Paragraph")]
    [InlineData("a\n\nb\n", "Paragraph Paragraph")]
    [InlineData("a\n\n\nb\n", "Paragraph Empty Paragraph")]
    [InlineData("a\n\n\n\nb\n", "Paragraph Empty Empty Paragraph")]
    [InlineData("\n\na\n", "Empty Empty Paragraph")]
    [InlineData("a\n\n", "Paragraph Empty")]
    [InlineData("a\n\n\n", "Paragraph Empty Empty")]
    [InlineData("", "Empty")]
    [InlineData("\n", "Empty")]
    [InlineData("# h\npara\n", "Heading Paragraph")]
    public void BlankLinesBecomeEmptyParagraphs(string markdown, string shape)
    {
        Assert.Equal(shape, Shape(RoundTrip(markdown).Children));
    }

    [Fact]
    public void FirstBlankLineIsTheSeparatorOfTheNextBlock()
    {
        var document = RoundTrip("a\n\n \n  \nb\n");
        Assert.Equal(["a", " ", "  ", "b"], document.Children.Cast<Paragraph>().Select(paragraph => paragraph.Text));
        Assert.Equal(new SyntaxLine(string.Empty, LinePrefix.None), Assert.Single(document.Children[3].SeparatorBefore!.Lines));
        Assert.Null(document.Children[1].SeparatorBefore);
    }

    [Fact]
    public void AdjacentBlocksRecordThatTheyHadNoBlankLine()
    {
        var document = RoundTrip("# h\npara\n");
        Assert.Equal(BlockSeparator.Adjacent(document.Children[0].Id), document.Children[1].SeparatorBefore);
    }

    [Fact]
    public void BlankLinesInsideABlockQuote()
    {
        var quote = Single<BlockQuote>(RoundTrip(">\n> a\n>\n>\n> b\n>\n"));
        Assert.Equal("Empty Paragraph Empty Paragraph Empty", Shape(quote.Children));
    }

    [Fact]
    public void BlankLinesBetweenListItemsStaySeparators()
    {
        var list = Single<ListBlock>(RoundTrip("- a\n\n\n- b\n"));
        Assert.Equal(2, list.Children[1].SeparatorBefore!.Lines.Length);
        Assert.All(list.Children.Cast<ListItem>(), item => Assert.Single(item.Children));
    }

    [Fact]
    public void ExtraBlankLinesInsideAnItemAreHeadlessEmptyParagraphs()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- a\n\n\n  b\n")).Children[0];
        Assert.Equal("Paragraph Empty Paragraph", Shape(item.Children));
    }

    [Fact]
    public void GeneratedEmptyParagraphsAddBlankLines()
    {
        var document = _build.Document(
            _build.Paragraph(string.Empty),
            _build.Paragraph("a"),
            _build.Paragraph(string.Empty),
            _build.Paragraph(string.Empty),
            _build.Paragraph("b"),
            _build.Paragraph(string.Empty));
        Assert.Equal("\na\n\n\n\nb\n\n", SerializeStable(document));
    }

    [Fact]
    public void GeneratedWhitespaceOnlyParagraphsSurvive()
    {
        var document = _build.Document(
            _build.Quote(_build.Paragraph("a"), _build.Paragraph(" "), _build.Paragraph("b")),
            _build.Bullets(_build.Item(_build.Paragraph("  "))));
        SerializeStable(document);
    }

    [Fact]
    public void AnEmptyParagraphNeedsTheSeparatorEvenIfTheBlocksWereAdjacent()
    {
        var document = Import("# h\npara\n");
        var changed = document with { Children = document.Children.Insert(1, _build.Paragraph(string.Empty)) };
        Assert.Equal("# h\n\n\npara\n", Serialize(changed));
    }

    private static string Shape(IEnumerable<Block> blocks) =>
        string.Join(' ', blocks.Select(static block => block is Paragraph { IsEmpty: true } ? "Empty" : block.Kind.ToString()));
}
