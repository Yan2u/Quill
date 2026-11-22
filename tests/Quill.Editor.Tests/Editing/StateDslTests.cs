using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// The state DSL itself: loading and dumping are inverse, markers land where they are written.
/// </summary>
public sealed class StateDslTests
{
    [Theory]
    [InlineData("<|>")]
    [InlineData("# <|>Hello")]
    [InlineData("<#h># <|>Hello")]
    [InlineData("<empty>\n\n<#h># <|>Hello")]
    [InlineData("a<^>bc<$>d")]
    [InlineData("a<$>bc<^>d")]
    [InlineData("one<^>\n\ntwo<$>")]
    [InlineData("- a\n- <|>b\n    - c")]
    [InlineData("```\ncode\n```\n\n<gap>\n\n---")]
    [InlineData("| a | b |\n| - | - |\n| <|> | |")]
    [InlineData("a\n\n<gap:^>\n\n---\n\n<gap:$>\n\nb")]
    public void LoadThenDumpReproducesTheText(string dsl)
    {
        var host = EditorTestHost.Load(dsl);
        Assert.Equal(dsl, host.Dump());
    }

    [Fact]
    public void CaretInHeadingText()
    {
        var host = EditorTestHost.Load("<#h># He<|>llo");
        var heading = Assert.IsType<Heading>(Assert.Single(host.State.Document.Children));
        Assert.Equal("Hello", heading.Text);
        Assert.Equal(host.Id("h"), heading.Id);
        Assert.Equal(TextSelection.Caret(heading.Id, 2), host.State.Selection);
    }

    [Fact]
    public void GapIsABoundaryPosition()
    {
        var host = EditorTestHost.Load("```\ncode\n```\n\n<gap>\n\n---");
        Assert.Equal(2, host.State.Document.Children.Count);
        Assert.Equal(TextSelection.Caret(new BoundaryPosition(host.State.Document.Id, 1)), host.State.Selection);
    }

    [Fact]
    public void LineWithOnlyACaretIsAnEmptyParagraph()
    {
        var host = EditorTestHost.Load("a\n\n<|>");
        var paragraph = Assert.IsType<Paragraph>(host.State.Document.Children[1]);
        Assert.Equal(string.Empty, paragraph.Text);
    }
}
