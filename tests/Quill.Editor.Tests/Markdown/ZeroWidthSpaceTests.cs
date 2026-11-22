using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// The leading zero-width space (Q28, Q34) keeps a paragraph whose first line starts with a tab or four
/// spaces from reopening as indented code; it is ordinary leaf text for Markdown I/O.
/// </summary>
public sealed class ZeroWidthSpaceTests
{
    private const char ZeroWidthSpace = '​';
    private readonly ModelBuilder _build = new();

    [Theory]
    [InlineData("​    indented text\n")]
    [InlineData("​\ttabbed text\n")]
    [InlineData("- ​\tfirst line of an item\n")]
    [InlineData("> ​    quoted\n")]
    public void ParagraphStartingWithZeroWidthSpaceRoundTrips(string markdown)
    {
        var document = RoundTrip(markdown);
        Assert.DoesNotContain(document.Children, block => block is CodeBlock);
    }

    [Fact]
    public void ZeroWidthSpaceIsPartOfTheLeafSource()
    {
        var paragraph = Single<Paragraph>(RoundTrip("​    text\n"));
        Assert.Equal(ZeroWidthSpace + "    text", paragraph.Text);
        Assert.False(paragraph.IsEmpty);
    }

    [Fact]
    public void GeneratedParagraphsWithZeroWidthSpaceStayParagraphs()
    {
        var document = _build.Document(
            _build.Paragraph(ZeroWidthSpace + "    a"),
            _build.Bullets(_build.Item(ZeroWidthSpace + "\tb")),
            _build.Quote(_build.Paragraph(ZeroWidthSpace + "    c")));
        SerializeStable(document);
    }
}
