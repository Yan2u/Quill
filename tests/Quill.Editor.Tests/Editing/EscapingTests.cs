using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Minimal escaping (Q50): text that would otherwise turn into syntax on saving gets the least change
/// that keeps it text, as Typora does; invisible leading spaces that would change the structure are
/// left out; indented code that would join a list is fenced.
/// </summary>
public sealed class EscapingTests
{
    [Theory]
    [InlineData("# a<|>", " #", "# a \\#<|>")]
    [InlineData("# a<|>", " ##", "# a \\##<|>")]
    [InlineData("# C<|>", "#", "# C#<|>")]
    [InlineData("## <|>", "#", "## \\#<|>")]
    public void HeadingClosingSequenceIsEscaped(string before, string typed, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Type(typed);
        Assert.Equal(after, host.Dump());
        var reopened = TestMarkdown.Import(TestMarkdown.Serialize(host.State.Document));
        Assert.Equal(TestMarkdown.Single<Heading>(host.State.Document).Text, TestMarkdown.Single<Heading>(reopened).Text);
    }

    // Typora escapes the first '$' of a "$$" line typed in a math block.
    [Fact]
    public void MathClosingLineIsEscaped()
    {
        var host = EditorTestHost.Load("$$\nx<|>\n$$");
        host.Press("Enter").Type("$$");
        Assert.Equal("$$\nx\n\\$$<|>\n$$", host.Dump());
        var reopened = TestMarkdown.Import(TestMarkdown.Serialize(host.State.Document));
        Assert.Equal("x\n\\$$", TestMarkdown.Single<MathBlock>(reopened).Text);
    }

    // Removing what stood between a list and an indented block: the block must not join the list.
    [Fact]
    public void IndentedCodeAfterListIsFenced()
    {
        var host = EditorTestHost.Load("- a<|>\n\n<!-- -->\n\n    code");
        host.Press("Delete Delete");
        string saved = TestMarkdown.Serialize(host.State.Document);
        Assert.Equal("- a\n\n```\ncode\n```", saved);
        Assert.Equal(ModelDump.Structure(host.State.Document), ModelDump.Structure(TestMarkdown.Import(saved)));
    }

    // Blank lines around indented code are not part of it: code whose first or last line became blank
    // is fenced.
    [Theory]
    [InlineData("    <|>code", "```\n\ncode\n```")]
    [InlineData("    code<|>", "```\ncode\n\n```")]
    public void IndentedCodeWithBlankEdgeLineIsFenced(string before, string saved)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Enter");
        Assert.Equal(saved, TestMarkdown.SerializeStable(host.State.Document));
    }

    // A rule's own indentation on a list item's marker line would widen the marker's spacing and move
    // the item's content column.
    [Fact]
    public void RuleIndentationOnItemLineIsLeftOut()
    {
        var document = TestMarkdown.Import("1. a\n\n    ---\n\n   b\n");
        var list = (ListBlock)document.Children[0];
        var item = (ListItem)list.Children[0];
        var edited = document with { Children = [list with { Children = [item with { Children = item.Children.RemoveAt(0) }] }] };
        Assert.Equal("1. ---\n\n   b\n", TestMarkdown.SerializeStable(edited));
    }

    // Text that would not read back as a Setext heading ("$$" opens a math block) makes the heading ATX.
    [Theory]
    [InlineData("a<|>\n===", "$$", "# $$")]
    [InlineData("a<|>\n---", "> q", "## > q")]
    [InlineData("a<|>\n---", "b", "b\n---")]
    public void SetextHeadingWhoseTextStartsABlockBecomesAtx(string before, string typed, string saved)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Backspace").Type(typed);
        Assert.Equal(saved, TestMarkdown.SerializeStable(host.State.Document));
    }

    // A committed paragraph keeps the syntax recorded in it under the ID it takes over, here the
    // indentation before a quote marker (a tab after it keeps its width).
    [Fact]
    public void CommittedQuoteKeepsItsRecordedPrefix()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type(" > a");
        host.Execute(new Commands.CommitParagraph());
        Assert.IsType<BlockQuote>(host.State.Document.Children[0]);
        Assert.Equal(" > a", TestMarkdown.Serialize(host.State.Document));
    }

    [Fact]
    public void LeadingSpacesAfterListAreLeftOut()
    {
        var host = EditorTestHost.Load("- a<|>\n\n<!-- -->\n\n  b");
        host.Press("Delete Delete");
        string saved = TestMarkdown.Serialize(host.State.Document);
        Assert.Equal("- a\n\nb", saved);
        Assert.IsType<Paragraph>(TestMarkdown.Import(saved).Children[1]);
    }

    [Fact]
    public void LeadingSpacesOnItemLineAreLeftOut()
    {
        var host = EditorTestHost.Load("- <|>a");
        host.Type("  ");
        Assert.Equal("- a", TestMarkdown.Serialize(host.State.Document));
    }

    [Fact]
    public void LeadingSpacesOfSetextHeadingOnItemLineAreLeftOut()
    {
        var host = EditorTestHost.Load("- <|>a\n  ---");
        host.Type("    ");
        string saved = TestMarkdown.Serialize(host.State.Document);
        Assert.Equal("- a\n  ---", saved);
        Assert.IsType<Heading>(((ListItem)TestMarkdown.Single<ListBlock>(TestMarkdown.Import(saved)).Children[0]).Children[0]);
    }

    // An empty first sub-item right after a paragraph is written without a blank line, as Typora writes
    // it; it interrupts the paragraph when read back (Typora, Q56), and the list stays tight.
    [Fact]
    public void EmptySubItemAfterParagraphSurvivesSaving()
    {
        var host = EditorTestHost.Load("- a<|>");
        host.Press("Enter Tab");
        string saved = TestMarkdown.Serialize(host.State.Document);
        Assert.Equal("- a\n    - ", saved);
        var reopened = TestMarkdown.Import(saved);
        Assert.Equal(ModelDump.Structure(host.State.Document), ModelDump.Structure(reopened));
        Assert.False(ListRules.IsLoose(TestMarkdown.Single<ListBlock>(reopened)));
    }

    // An item emptied by editing is written like a new empty item: its marker and one space (Typora).
    [Theory]
    [InlineData("- abc<|>", "- ")]
    [InlineData("1.  abc<|>", "1. ")]
    public void EmptiedItemKeepsOneSpace(string before, string saved)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Ctrl+Backspace");
        Assert.Equal(saved, TestMarkdown.Serialize(host.State.Document));
    }

    // Typora keeps the headless items of an item whose text was deleted: the marker line is written
    // alone, the blocks follow after a blank line, and reopening gives the same shape.
    [Theory]
    [InlineData("- a<|>\n\n  b", "- \n\n  b")]
    [InlineData("- x\n- a<|>\n\n  b\n- y", "- x\n- \n\n  b\n- y")]
    [InlineData("- a<|>\n    - b", "- \n\n    - b")]
    [InlineData("- [ ] a<|>\n\n  b", "- [ ] \n\n  b")]
    [InlineData("- [x] a<|>\n    - b", "- [x] \n    - b")]
    public void EmptiedItemKeepsItsBlocks(string before, string saved)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Backspace");
        Assert.Equal(saved, TestMarkdown.SerializeStable(host.State.Document));
    }
}
