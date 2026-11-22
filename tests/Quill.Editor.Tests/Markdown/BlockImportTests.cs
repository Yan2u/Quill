using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Import of each block type (02-document-model.md, 06-markdown-io.md): leaf sources exclude block
/// syntax and container prefixes; syntax is kept as metadata; serializing reproduces the input.
/// </summary>
public sealed class BlockImportTests
{
    [Fact]
    public void ParagraphKeepsLineBreaksLeadingAndTrailingWhitespace()
    {
        var paragraph = Single<Paragraph>(RoundTrip("  foo  \n bar\n"));
        Assert.Equal("  foo  \n bar", paragraph.Text);
    }

    [Theory]
    [InlineData("# Foo\n", 1, "Foo", "# ", "")]
    [InlineData("## Foo ##\n", 2, "Foo", "## ", " ##")]
    [InlineData("   ###   Foo   \n", 3, "Foo", "   ###   ", "   ")]
    [InlineData("# Foo \\#\n", 1, "Foo \\#", "# ", "")]
    [InlineData("### ###\n", 3, "", "### ", "###")]
    [InlineData("#\n", 1, "", "#", "")]
    public void AtxHeadingSplitsMarkersFromContent(string markdown, int level, string text, string opening, string closing)
    {
        var heading = Single<Heading>(RoundTrip(markdown));
        Assert.Equal(level, heading.Level);
        Assert.Equal(text, heading.Text);
        Assert.Equal(new HeadingSyntax { Style = HeadingStyle.Atx, Level = level, Opening = opening, Closing = closing }, heading.Syntax);
    }

    [Fact]
    public void SetextHeadingKeepsUnderlineAndOuterWhitespaceAsSyntax()
    {
        var heading = Single<Heading>(RoundTrip("  Foo  \n-----\n"));
        Assert.Equal(2, heading.Level);
        Assert.Equal("Foo", heading.Text);
        Assert.Equal(HeadingStyle.Setext, heading.Syntax!.Style);
        Assert.Equal("  ", heading.Syntax.Opening);
        Assert.Equal("  ", heading.Syntax.Closing);
        Assert.Equal("-----", heading.Syntax.Underline!.Value.Text);
    }

    [Fact]
    public void FencedCodeKeepsInfoAndStripsFenceIndentation()
    {
        var code = Single<CodeFence>(RoundTrip("  ```js extra\n  a\n b\n    c\n  ```\n"));
        Assert.Equal("js extra", code.Info);
        Assert.Equal("a\nb\n  c", code.Code.Text);
        Assert.Equal(CodeBlockStyle.Fenced, code.Syntax!.Style);
        Assert.Equal(2, code.Syntax.Fence!.IndentColumns);
        Assert.Equal("```", code.Syntax.Fence.Closing!.Value.Text.Trim());
    }

    [Theory]
    [InlineData("```\n```\n", "", true)]
    [InlineData("```\n\n```\n", "", false)]
    [InlineData("~~~~\ncode\n~~~~\n", "code", false)]
    public void FencedCodeDistinguishesNoLineFromAnEmptyLine(string markdown, string text, bool noLines)
    {
        var code = Single<CodeFence>(RoundTrip(markdown));
        Assert.Equal(text, code.Code.Text);
        Assert.Equal(noLines, code.Syntax!.Fence!.HasNoContentLines);
    }

    [Fact]
    public void UnclosedFenceStaysUnclosed()
    {
        var code = Single<CodeFence>(RoundTrip("```\ncode\n"));
        Assert.Equal("code", code.Code.Text);
        Assert.Null(code.Syntax!.Fence!.Closing);
    }

    [Fact]
    public void IndentedCodeStripsFourColumnsIncludingTabs()
    {
        var code = Single<CodeFence>(RoundTrip("    a\n\tb\n\n      c\n"));
        Assert.Equal("a\nb\n\n  c", code.Code.Text);
        Assert.Equal(CodeBlockStyle.Indented, code.Syntax!.Style);
    }

    [Fact]
    public void MathBlockHoldsTheTexSource()
    {
        var math = Single<MathBlock>(RoundTrip("$$\nx^2\n\\frac{a}{b}\n$$\n"));
        Assert.Equal("x^2\n\\frac{a}{b}", math.Text);
    }

    [Fact]
    public void HtmlBlockKeepsItsLinesVerbatim()
    {
        var html = Single<HtmlBlock>(RoundTrip("<div>\n  <b>hi</b>\n</div>\n"));
        Assert.Equal("<div>\n  <b>hi</b>\n</div>", html.Text);
    }

    [Fact]
    public void FrontMatterAtTheStartOfTheDocument()
    {
        var document = RoundTrip("---\ntitle: x\ntags: [a]\n---\nbody\n");
        var frontMatter = Assert.IsType<FrontMatter>(document.Children[0]);
        Assert.Equal("title: x\ntags: [a]", frontMatter.Text);
        Assert.Equal("body", Assert.IsType<Paragraph>(document.Children[1]).Text);
    }

    [Theory]
    [InlineData("---\n")]
    [InlineData(" * * *\n")]
    [InlineData("___\n")]
    public void ThematicBreakKeepsItsLine(string markdown)
    {
        var rule = Single<ThematicBreak>(RoundTrip(markdown));
        Assert.Equal(markdown.TrimEnd('\n'), rule.Syntax!.Value.Text);
    }

    [Theory]
    [InlineData("[toc]\n")]
    [InlineData("[TOC]\n")]
    [InlineData("  [Toc]  \n")]
    public void TocMarkerIsABlock(string markdown)
    {
        Single<TocBlock>(RoundTrip(markdown));
    }

    [Fact]
    public void TocDoesNotInterruptAParagraph()
    {
        Assert.Equal("text\n[toc]", Single<Paragraph>(RoundTrip("text\n[toc]\n")).Text);
    }

    [Fact]
    public void BlockQuoteRecordsPrefixesIncludingLazyLines()
    {
        var quote = Single<BlockQuote>(RoundTrip("> a\nb\n>  c\n"));
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(quote.Children));
        Assert.Equal("a\nb\n c", paragraph.Text);
        Assert.True(paragraph.LinePrefixes[0].TryGetSegment(quote.Id, out var first));
        Assert.Equal("> ", first);
        Assert.True(paragraph.LinePrefixes[1].TryGetSegment(quote.Id, out var lazy));
        Assert.Equal(string.Empty, lazy);
    }

    [Fact]
    public void NestedBlockQuotes()
    {
        var outer = Single<BlockQuote>(RoundTrip("> > a\n> b\n"));
        var inner = Assert.IsType<BlockQuote>(Assert.Single(outer.Children));
        Assert.Equal("a\nb", Assert.IsType<Paragraph>(Assert.Single(inner.Children)).Text);
    }

    [Fact]
    public void TableCellsExcludePipesAndPadding()
    {
        var table = Single<Table>(RoundTrip("| a | b |\n|:-|-:|\n| 1 | 2 \\| 3 |\n"));
        Assert.Equal([TableAlignment.Left, TableAlignment.Right], table.Alignments);
        var cells = table.Children.Cast<TableRow>().Select(row => row.Children.Cast<TableCell>().Select(cell => cell.Text).ToArray()).ToArray();
        Assert.Equal([["a", "b"], ["1", "2 \\| 3"]], cells);
    }

    [Fact]
    public void TablePadsShortRowsAndKeepsExtraCellsAsRowText()
    {
        var table = Single<Table>(RoundTrip("| a | b |\n| - | - |\n| 1 |\n| 2 | 3 | 4 |\n"));
        Assert.Equal(2, table.ColumnCount);
        var shortRow = (TableRow)table.Children[1];
        Assert.Equal(["1", ""], shortRow.Children.Cast<TableCell>().Select(cell => cell.Text));
        var longRow = (TableRow)table.Children[2];
        Assert.Equal(["2", "3"], longRow.Children.Cast<TableCell>().Select(cell => cell.Text));
        Assert.Equal(" | 4 |", longRow.Syntax!.Gaps[^1]);
    }

    [Fact]
    public void LinkReferenceDefinitionHasThreeSlots()
    {
        var definition = Single<LinkReferenceDefinition>(RoundTrip("[Foo Bar]: http://example.com \"The title\"\n"));
        Assert.Equal("Foo Bar", definition.Label.Text);
        Assert.Equal("http://example.com", definition.Destination.Text);
        Assert.Equal("The title", definition.Title.Text);
        Assert.True(definition.Syntax!.HasTitle);
        Assert.All(definition.Children.Cast<DefinitionSlot>(), slot => Assert.Equal(InlineSyntaxSet.PlainText, slot.InlineSyntax));
    }

    [Fact]
    public void LinkReferenceDefinitionWithPointyDestinationAndNoTitle()
    {
        var definition = Single<LinkReferenceDefinition>(RoundTrip("[a]: <my url>\n"));
        Assert.Equal("my url", definition.Destination.Text);
        Assert.Equal(string.Empty, definition.Title.Text);
        Assert.True(definition.Syntax!.IsPointy);
        Assert.False(definition.Syntax.HasTitle);
    }

    [Fact]
    public void LinkReferenceDefinitionSpanningLinesInsideAQuote()
    {
        var quote = Single<BlockQuote>(RoundTrip("> [foo]:\n> /url\n> 'the\n> title'\n"));
        var definition = Assert.IsType<LinkReferenceDefinition>(Assert.Single(quote.Children));
        Assert.Equal("/url", definition.Destination.Text);
        Assert.Equal("the\ntitle", definition.Title.Text);
    }

    [Fact]
    public void LinkReferenceDefinitionsStayInPlaceBeforeTheParagraph()
    {
        var document = RoundTrip("[a]: /x\n[b]: /y\ntext\n");
        Assert.Equal([BlockKind.LinkReferenceDefinition, BlockKind.LinkReferenceDefinition, BlockKind.Paragraph], document.Children.Select(block => block.Kind));
        Assert.Equal("text", ((Paragraph)document.Children[2]).Text);
    }

    [Fact]
    public void FootnoteDefinitionHasNameAndContentSlots()
    {
        var footnote = Single<FootnoteDefinition>(RoundTrip("[^note]: Footnote text.\n"));
        Assert.Equal("note", footnote.Name.Text);
        Assert.Equal("Footnote text.", footnote.Content.Text);
        Assert.Equal(2, footnote.Children.Count);
        Assert.Equal(InlineSyntaxSet.PlainText, footnote.Name.InlineSyntax);
        Assert.Equal(InlineSyntaxSet.FootnoteContent, footnote.Content.InlineSyntax);
    }

    [Fact]
    public void UnreferencedFootnotesAreKeptInSourceOrder()
    {
        var document = RoundTrip("a\n\n[^1]: one\n\nb\n\n[^2]: two\n");
        Assert.Equal(
            [BlockKind.Paragraph, BlockKind.FootnoteDefinition, BlockKind.Paragraph, BlockKind.FootnoteDefinition],
            document.Children.Select(block => block.Kind));
    }

    // Typora: an empty name is a footnote definition too.
    [Theory]
    [InlineData("[^]: note\n", "note")]
    [InlineData("[^]:\n", "")]
    public void FootnoteNameMayBeEmpty(string markdown, string content)
    {
        var footnote = Single<FootnoteDefinition>(RoundTrip(markdown));
        Assert.Equal((string.Empty, content), (footnote.Name.Text, footnote.Content.Text));
    }

    // The description slot is inline text (Q44): the definition line holds text even where it reads
    // as block syntax.
    [Theory]
    [InlineData("[^1]: - x\n", "- x")]
    [InlineData("[^1]: # x\n", "# x")]
    [InlineData("[^1]: > x\n", "> x")]
    [InlineData("[^1]: 1. x\n", "1. x")]
    [InlineData("[^1]: [toc]\n", "[toc]")]
    [InlineData("[^1]: ***\n", "***")]
    [InlineData("[^1]: \t# x\n", "# x")]
    public void FootnoteContentIsTextEvenWhenItLooksLikeABlock(string markdown, string content)
    {
        var footnote = Single<FootnoteDefinition>(RoundTrip(markdown));
        Assert.Equal(content, footnote.Content.Text);
        Assert.Equal(2, footnote.Children.Count);
    }

    [Fact]
    public void FootnoteContentIsOneLine()
    {
        // Q30: later lines of the first paragraph become a paragraph of their own after the slots.
        var footnote = Single<FootnoteDefinition>(RoundTrip("[^1]: first\nlazy\n"));
        Assert.Equal("first", footnote.Content.Text);
        var rest = Assert.IsType<Paragraph>(footnote.Children[2]);
        Assert.Equal("lazy", rest.Text);
        Assert.True(rest.SeparatorBefore!.IsAdjacent);
    }

    [Fact]
    public void MultiParagraphFootnoteKeepsLaterParagraphsIndented()
    {
        var footnote = Single<FootnoteDefinition>(RoundTrip("[^1]: first\n\n    second\n\n    third\n"));
        Assert.Equal("first", footnote.Content.Text);
        Assert.Equal(["second", "third"], footnote.Children.Skip(2).Cast<Paragraph>().Select(paragraph => paragraph.Text));
    }
}
