using Quill.Editor.Documents;
using Quill.Editor.Statistics;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Statistics;

/// <summary>M6.5: word count of the displayed text and the outline of the headings.</summary>
public sealed class StatisticsTests
{
    [Theory]
    [InlineData("", 0, 0, 0)]
    [InlineData("Hello, world!", 2, 12, 1)]
    [InlineData("don't stop-motion 42", 3, 18, 1)]
    [InlineData("中文与 English 混排", 6, 12, 1)]
    [InlineData("ひらがな한글", 6, 6, 1)]
    [InlineData("a\n\n  \nb c", 3, 3, 2)]
    public void TextIsCounted(string text, int words, int characters, int lines) =>
        Assert.Equal(new TextCounts(words, characters, lines), WordStatistics.Count(text));

    [Fact]
    public void MarkdownSyntaxIsNotCounted()
    {
        var host = EditorTestHost.Load("# Title\n\n**bold** and [link](http://example.com/a/b) ![alt](img.png)\n\n- item\n\n<|>");
        var counts = WordStatistics.Of(host.State.Document);
        Assert.Equal(6, counts.Words);   // Title, bold, and, link, alt, item
    }

    [Fact]
    public void CodeAndMathCountFrontMatterAndLanguageDoNot()
    {
        var host = EditorTestHost.Load("---\ntitle: x\n---\n\n```js\nlet a\n```\n\n$$\nx\n$$\n\n<|>");
        Assert.Equal(3, WordStatistics.Of(host.State.Document).Words);   // let, a, x
    }

    [Fact]
    public void EditsChangeTheCount()
    {
        var host = EditorTestHost.Load("one two<|>");
        Assert.Equal(2, WordStatistics.Of(host.State.Document).Words);
        host.Type(" three 四五");
        Assert.Equal(5, WordStatistics.Of(host.State.Document).Words);
    }

    [Fact]
    public void TheSelectionIsCounted()
    {
        var host = EditorTestHost.Load("one <^>two\n\nthree **fo<$>ur**");
        // A selection ending inside "**four**" leaves its opening "**" unmatched: words are exact, the
        // two marker characters count as characters (a known limit of counting selected source).
        Assert.Equal(new TextCounts(3, 12, 2), WordStatistics.OfSelection(host.State));
        Assert.Null(WordStatistics.OfSelection(EditorTestHost.Load("a<|>").State));
    }

    [Fact]
    public void ReadingTimeIsRounded()
    {
        Assert.Equal(6, new TextCounts(2280, 0, 0).MinutesAt(WordStatistics.DefaultWordsPerMinute));
        Assert.Equal(19, new TextCounts(2280, 0, 0).MinutesAt(120));
        Assert.Equal(10, new TextCounts(2280, 0, 0).MinutesAt(240));
        Assert.Equal(1, new TextCounts(3, 0, 0).MinutesAt(382));
        Assert.Equal(0, default(TextCounts).MinutesAt(382));
        Assert.Equal(2280, new TextCounts(2280, 0, 0).MinutesAt(0));
    }

    [Fact]
    public void TheOutlineListsHeadingsWithTheirShownText()
    {
        var host = EditorTestHost.Load("# One *a*\n\ntext\n\n- item\n\n  ## Two\n\n### Three\n\n<|>");
        var outline = DocumentOutline.Of(host.State.Document);
        Assert.Equal([(1, "One a"), (2, "Two"), (3, "Three")], outline.Select(static entry => (entry.Level, entry.Text)));
    }

    [Fact]
    public void TheCurrentHeadingIsTheLastBeforeTheBlockInView()
    {
        var host = EditorTestHost.Load("intro\n\n# One\n\na\n\n## Two\n\nb\n\n<|>");
        var document = host.State.Document;
        var outline = DocumentOutline.Of(document);
        BlockId Block(int index) => document.Children[index].Id;
        Assert.Null(DocumentOutline.CurrentHeading(document, outline, Block(0)));
        Assert.Equal(outline[0].Id, DocumentOutline.CurrentHeading(document, outline, Block(1)));
        Assert.Equal(outline[0].Id, DocumentOutline.CurrentHeading(document, outline, Block(2)));
        Assert.Equal(outline[1].Id, DocumentOutline.CurrentHeading(document, outline, Block(4)));
    }
}
