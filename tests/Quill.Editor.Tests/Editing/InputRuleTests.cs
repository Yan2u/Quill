using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Paragraphs and block syntax (02, 04, Q24, Q28, Q33, Q42): input rules after any edit, Enter rules,
/// re-parsing on commit, and the leading zero-width space.
/// </summary>
public sealed class InputRuleTests
{
    [Theory]
    [InlineData("# ", "# <|>")]
    [InlineData("### a", "### a<|>")]
    [InlineData("> ", "> <|>")]
    [InlineData("> a", "> a<|>")]
    [InlineData("- ", "- <|>")]
    [InlineData("* a", "* a<|>")]
    [InlineData("+ a", "+ a<|>")]
    [InlineData("1. a", "1. a<|>")]
    [InlineData("7) a", "7) a<|>")]
    [InlineData("- [ ] a", "- [ ] a<|>")]
    [InlineData("- [x] a", "- [x] a<|>")]
    public void TypingBlockSyntaxAtParagraphStartConverts(string typed, string after)
    {
        var host = EditorTestHost.Load("<|>");
        host.Type(typed);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("# ", typeof(Heading))]
    [InlineData("> ", typeof(BlockQuote))]
    [InlineData("- ", typeof(ListBlock))]
    [InlineData("1. ", typeof(ListBlock))]
    public void ConversionsMakeBlocks(string typed, Type kind)
    {
        var host = EditorTestHost.Load("<|>");
        host.Type(typed);
        Assert.IsType(kind, Assert.Single(host.State.Document.Children));
    }

    // Deleting the "x" of "x# a" makes the paragraph start with "# ": it converts at once (Q24).
    [Fact]
    public void EditThatExposesSyntaxConverts()
    {
        var host = EditorTestHost.Load("x<|># a");
        host.Press("Backspace");
        Assert.Equal("# <|>a", host.Dump());
        Assert.IsType<Heading>(Assert.Single(host.State.Document.Children));
    }

    // The conversion is its own undo item: the first undo keeps the typed characters.
    [Fact]
    public void UndoRevertsOnlyTheConversion()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("# ");
        host.Press("Ctrl+Z");
        var paragraph = Assert.IsType<Paragraph>(Assert.Single(host.State.Document.Children));
        Assert.Equal("# ", paragraph.Text);
        host.Press("Ctrl+Z");
        Assert.Equal("<|>", host.Dump());
    }

    [Theory]
    [InlineData("$$<|>", "$$\n<|>\n$$")]
    [InlineData("a\n\n---<|>", "a\n\n---\n\n<|>")]
    [InlineData("a\n\n***<|>", "a\n\n***\n\n<|>")]
    [InlineData("[toc]<|>", "[toc]\n\n<|>")]
    public void EnterRules(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Enter");
        Assert.Equal(after, host.Dump());
    }

    // A paragraph holding only a fence cannot be written as Markdown (it opens a code block), so it is typed.
    [Theory]
    [InlineData("```js", "```js\n<|>\n```")]
    [InlineData("~~~", "```\n<|>\n```")]
    public void EnterAfterFenceMakesCodeBlock(string typed, string after)
    {
        var host = EditorTestHost.Load("<|>");
        host.Type(typed).Press("Enter");
        Assert.Equal(after, host.Dump());
        Assert.IsType<CodeFence>(Assert.Single(host.State.Document.Children));
    }

    [Fact]
    public void EnterAfterTableHeaderMakesTable()
    {
        var host = EditorTestHost.Load("| a | b |<|>");
        host.Press("Enter");
        var table = TestMarkdown.Single<Table>(host.State.Document);
        Assert.Equal(2, table.ColumnCount);
        Assert.Equal(["a", "b"], ((TableRow)table.Children[0]).Children.Select(cell => ((TableCell)cell).Text));
        var firstBodyCell = ((TableRow)table.Children[1]).Children[0];
        Assert.Equal(State.TextSelection.Caret(firstBodyCell.Id, 0), host.State.Selection);
        Assert.Equal("| a | b |\n| --- | --- |\n| <|> |  |", host.Dump());
    }

    [Fact]
    public void FenceRuleIsItsOwnUndoItem()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("```");
        host.Press("Enter");
        Assert.IsType<CodeFence>(Assert.Single(host.State.Document.Children));
        host.Press("Ctrl+Z");
        Assert.Equal("```<|>", host.Dump());
    }

    // Committing re-parses the paragraph (Q28, Q33, Q42): an underline after a soft break makes the
    // line above it a heading, the lines above stay a paragraph, and a blank line is added between.
    [Fact]
    public void EnterCommitsSetextUnderline()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("x").Press("Shift+Enter").Type("a").Press("Shift+Enter").Type("===").Press("Enter");
        Assert.Equal("x\n\na\n===\n\n<|>", host.Dump());
        var heading = Assert.IsType<Heading>(host.State.Document.Children[1]);
        Assert.Equal((1, "a"), (heading.Level, heading.Text));
    }

    [Fact]
    public void DashesThenEnterAsFirstBlockStartFrontMatter()
    {
        var host = EditorTestHost.Load("<|>\n\ntext");
        host.Type("---").Press("Enter").Type("title: a");
        var front = Assert.IsType<FrontMatter>(host.State.Document.Children[0]);
        Assert.Equal("title: a", front.Text);
        Assert.IsType<Paragraph>(host.State.Document.Children[1]);
        Assert.Equal("---\ntitle: a\n---\n\ntext", TestMarkdown.Serialize(host.State.Document));

        host.Press("Ctrl+Z");
        Assert.IsType<FrontMatter>(host.State.Document.Children[0]);
        host.Press("Ctrl+Z").Press("Ctrl+Z");
        Assert.IsType<Paragraph>(host.State.Document.Children[0]);
    }

    [Theory]
    [InlineData("first\n\n<|>")]   // not the first block
    [InlineData("- <|>")]            // inside a list (Q47)
    public void DashesThenEnterElsewhereMakeARule(string document)
    {
        var host = EditorTestHost.Load(document);
        host.Type("---").Press("Enter");
        Assert.DoesNotContain(host.State.Document.Children, static block => block is FrontMatter);
    }

    [Fact]
    public void OtherRulesAsFirstBlockStayRules()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("***").Press("Enter");
        Assert.IsType<ThematicBreak>(host.State.Document.Children[0]);
    }

    [Fact]
    public void DashesThenLeavingAsFirstBlockMakeARule()
    {
        var host = EditorTestHost.Load("<|>\n\ntext");
        host.Type("---").Press("Down");
        Assert.IsType<ThematicBreak>(host.State.Document.Children[0]);
    }

    [Fact]
    public void LeavingParagraphCommitsIt()
    {
        var host = EditorTestHost.Load("first\n\n<|>");
        host.Type("x").Press("Shift+Enter").Type("---").Press("Ctrl+Home");
        Assert.Equal("<|>first\n\nx\n---", host.Dump());
        Assert.IsType<Heading>(host.State.Document.Children[1]);
    }

    [Fact]
    public void SoftBreakThenHeadingSyntaxSplitsOnCommit()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("a").Press("Shift+Enter").Type("# b").Press("Enter");
        Assert.Equal("a\n\n# b\n\n<|>", host.Dump());
    }

    // List and quote syntax after a soft break also become blocks of their own, after a blank line
    // (Typora's auto-formatting); the Enter then acts in the new block.
    [Theory]
    [InlineData("- b", "a\n\n- b\n- <|>")]
    [InlineData("> b", "a\n\n> b\n>\n> <|>")]
    [InlineData("1. b", "a\n\n1. b\n2. <|>")]
    public void SoftBreakThenBlockSyntaxSplitsOnCommit(string typed, string after)
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("a").Press("Shift+Enter").Type(typed).Press("Enter");
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void CommitIsItsOwnUndoItem()
    {
        var host = EditorTestHost.Load("first\n\n<|>");
        host.Type("x").Press("Shift+Enter").Type("---").Execute(new Commands.CommitParagraph());
        Assert.IsType<Heading>(host.State.Document.Children[1]);
        host.Press("Ctrl+Z");
        var paragraph = Assert.IsType<Paragraph>(host.State.Document.Children[1]);
        Assert.Equal("x\n---", paragraph.Text);
    }

    [Fact]
    public void LosingFocusDoesNotCommit()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("x").Press("Shift+Enter").Type("---");
        host.Execute(new Commands.SetInteraction(State.Interaction.Default with { HasFocus = false }));
        Assert.IsType<Paragraph>(Assert.Single(host.State.Document.Children));
    }

    // A leading tab or four spaces get a zero-width space in front (Q28, Q34); it goes away with them.
    [Theory]
    [InlineData("<|>a", "Tab", "​\t<|>a")]
    [InlineData("​\t<|>a", "Backspace", "<|>a")]
    [InlineData("<|>​\ta", "Delete", "<|>a")]
    [InlineData("- <|>a", "Tab", "- ​\t<|>a")]
    public void LeadingZeroWidthSpace(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void FourSpacesGetZeroWidthSpaceAndSurviveReopening()
    {
        var host = EditorTestHost.Load("<|>a");
        host.Type("    ");
        Assert.Equal("​    <|>a", host.Dump());
        string saved = TestMarkdown.Serialize(host.State.Document);
        var reopened = TestMarkdown.Import(saved);
        Assert.Equal("​    a", TestMarkdown.Single<Paragraph>(reopened).Text);
    }
}
