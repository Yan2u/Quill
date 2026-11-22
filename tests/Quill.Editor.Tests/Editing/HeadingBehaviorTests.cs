using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Headings, 04 behavior catalog: Enter at the start, in the middle and at the end; Shift+Enter is
/// Enter; Backspace at the start turns the heading into a paragraph keeping its ID.
/// </summary>
public sealed class HeadingBehaviorTests
{
    // Enter in a heading; see the behavior catalog in 04-commands-and-behaviors.md.
    [Theory]
    [InlineData("<#h># <|>Hello", "<empty>\n\n<#h># <|>Hello")]
    [InlineData("# Hel<|>lo", "# Hel\n\n# <|>lo")]
    [InlineData("# Hello<|>", "# Hello\n\n<|>")]
    [InlineData("## Hel<|>lo", "## Hel\n\n## <|>lo")]
    public void EnterInHeading(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Enter");
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("<#h># <|>Hello", "<empty>\n\n<#h># <|>Hello")]
    [InlineData("# Hel<|>lo", "# Hel\n\n# <|>lo")]
    [InlineData("# Hello<|>", "# Hello\n\n<|>")]
    public void ShiftEnterInHeadingIsEnter(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Shift+Enter");
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void SecondHalfOfSplitHeadingGetsNewId()
    {
        var host = EditorTestHost.Load("<#h># Hel<|>lo");
        host.Press("Enter");
        Assert.Equal(host.Id("h"), host.State.Document.Children[0].Id);
        Assert.NotEqual(host.Id("h"), host.State.Document.Children[1].Id);
    }

    [Theory]
    [InlineData("<#h># <|>Hello", "<#h><|>Hello")]
    [InlineData("a\n\n<#h>### <|>Hello", "a\n\n<#h><|>Hello")]
    public void BackspaceAtHeadingStartMakesParagraphKeepingId(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Backspace");
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("# Hello<|>\n\nworld", "# Hello<|>world")]
    [InlineData("# Hello<|>\n\n## world", "# Hello<|>world")]
    public void DeleteAtHeadingEndMergesNextTextBlock(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Delete");
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("a<|>", "Ctrl+2", "## a<|>")]
    [InlineData("## a<|>", "Ctrl+0", "a<|>")]
    [InlineData("## a<|>", "Ctrl+4", "#### a<|>")]
    public void HeadingShortcuts(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }
}
