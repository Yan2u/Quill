using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Empty paragraphs in list items as Typora writes and reads them (Q56): a line holding only the
/// item's indentation, with blank lines separating it, as in "- a", a blank line, then "  ". Markdown
/// itself has no empty paragraph; CommonMark reads those lines as blank lines after the list.
/// </summary>
public sealed class ListEmptyParagraphTests
{
    [Theory]
    [InlineData("- a\n\n  ")]
    [InlineData("- a\n\n  \n")]
    [InlineData("1. a\n\n   \n")]
    [InlineData("> - a\n>\n>   \n")]
    public void EmptyParagraphAtTheEndOfAnItem(string markdown)
    {
        var item = FirstItem(RoundTrip(markdown));
        Assert.Equal(2, item.Children.Count);
        Assert.True(Assert.IsType<Paragraph>(item.Children[1]).IsEmpty);
    }

    [Theory]
    [InlineData("- a\n\n  \n\n- b\n", 2)]
    [InlineData("- a\n\n  \n\n  \n", 3)]
    [InlineData("- a\n\n  \n\n  b\n", 3)]
    public void EmptyParagraphsBetweenBlocksAndItems(string markdown, int children)
    {
        var document = RoundTrip(markdown);
        var item = FirstItem(document);
        Assert.Equal(children, item.Children.Count);
        Assert.True(((Paragraph)item.Children[1]).IsEmpty);
    }

    // An item whose text is empty and that holds only empty paragraphs opens with a blank line (Q53).
    [Fact]
    public void EmptyItemWithEmptyParagraphs()
    {
        var item = FirstItem(RoundTrip("- \n\n  \n"));
        Assert.True(item.Syntax!.OpensWithBlankLine);
        Assert.All(item.Children, static child => Assert.True(((Paragraph)child).IsEmpty));
        Assert.Equal(2, item.Children.Count);
    }

    // Quill's own earlier form, an extra blank line between blocks, still reads as an empty paragraph
    // and is kept as written.
    [Fact]
    public void ExtraBlankLineInAnItemIsAnEmptyParagraph()
    {
        var item = FirstItem(RoundTrip("- a\n\n\n  b\n"));
        Assert.Equal(["a", "", "b"], item.Children.Cast<Paragraph>().Select(static paragraph => paragraph.Text));
    }

    // A blank line right after a block separates, however far it is indented.
    [Theory]
    [InlineData("- a\n  \n- b\n")]
    [InlineData("- a\n  \n  b\n")]
    public void IndentedSeparatorIsNotAnEmptyParagraph(string markdown)
    {
        var item = FirstItem(RoundTrip(markdown));
        Assert.DoesNotContain(item.Children, static child => child is Paragraph { IsEmpty: true });
    }

    // Backspace at the start of an empty item makes it an empty headless item of the item above,
    // which Typora saves as "  " after a blank line.
    [Theory]
    [InlineData("- a\n- <|>", "Backspace")]
    [InlineData("- a<|>", "Ctrl+Enter")]
    [InlineData("- a\n    - <|>", "Enter")]
    public void NewEmptyHeadlessItemIsSavedAsTyporaWritesIt(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal("- a\n\n  ", SerializeStable(host.State.Document));
    }

    [Fact]
    public void EmptyHeadlessItemBetweenItems()
    {
        var host = EditorTestHost.Load("- a\n- <|>\n- c");
        host.Press("Backspace");
        Assert.Equal("- a\n\n  \n\n- c", SerializeStable(host.State.Document));
    }

    // Whitespace of an empty paragraph right after a list is left out where the line would become an
    // empty paragraph of the list's last item; further on it cannot, and stays.
    [Theory]
    [InlineData(new[] { "   ", "b" }, "- a\n\n\nb\n")]
    [InlineData(new[] { "", "   ", "b" }, "- a\n\n\n   \nb\n")]
    public void WhitespaceParagraphAfterListStaysOut(string[] paragraphs, string saved)
    {
        var builder = new ModelBuilder();
        var document = builder.Document([builder.Bullets(builder.Item("a")), .. paragraphs.Select(builder.Paragraph)]);
        Assert.Equal(saved, Serialize(document));
        var reopened = Import(saved);
        Assert.Equal(paragraphs.Length + 1, reopened.Children.Count);
        Assert.Single(FirstItem(reopened).Children);
    }

    private static ListItem FirstItem(Document document)
    {
        var block = document.Children[0] is BlockQuote quote ? quote.Children[0] : document.Children[0];
        return (ListItem)((ListBlock)block).Children[0];
    }
}
