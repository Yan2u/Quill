using Quill.Editor.Commands;
using Quill.Editor.Input;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Copy and cut without a selection (04, Q110, Q116), with the setting on: the text block at the caret, or
/// the line in a code or math block. Cutting removes the block, or the line with its line break; a table
/// cell is cleared. Off (the default), a caret copies nothing and cuts nothing.
/// </summary>
public sealed class LineClipboardTests
{
    private static readonly EditorOptions Lines = EditorOptions.Default with { CopyLineWithoutSelection = true };

    [Theory]
    [InlineData("x\n\na<|>b\n\ny", "ab", "ab")]
    [InlineData("# h<|>i\n\ny", "# hi", "# hi")]
    [InlineData("- a\n- b<|>c", "bc", "bc")]
    [InlineData("a **b<|>**", "a **b**", "a **b**")]
    [InlineData("```\none\ntw<|>o\nthree\n```", "two", "two")]
    [InlineData("```\n<|>one\n```", "one", "one")]
    [InlineData("$$\nx\ny<|>\n$$", "y", "y")]
    [InlineData("| a<|> | b |\n| - | - |", "a", "a")]
    public void ACaretCopiesItsBlockOrLine(string state, string markdown, string plain)
    {
        var host = EditorTestHost.Load(state, Lines).Press("Ctrl+C");
        Assert.Equal(new ClipboardContent(markdown, plain), host.Clipboard);
        Assert.Equal(state, host.Dump());
    }

    [Fact]
    public void TheShownTextFollowsTheOtherSetting()
    {
        var host = EditorTestHost.Load("# a **b<|>**", Lines with { CopyMarkdownSource = false }).Press("Ctrl+C");
        Assert.Equal(new ClipboardContent("# a **b**", "a b"), host.Clipboard);
        host.Run(ClipboardAction.CopyAsPlainText);
        Assert.Equal(new ClipboardContent(null, "a b"), host.Clipboard);
    }

    [Fact]
    public void WithTheSettingOffACaretCopiesNothing()
    {
        var host = EditorTestHost.Load("a<|>b").Press("Ctrl+C");
        Assert.Equal(new ClipboardContent(null, string.Empty), host.Clipboard);
        Assert.Equal("a<|>b", host.Press("Ctrl+X").Dump());
    }

    [Theory]
    [InlineData("x\n\na<|>b\n\ny", "x\n\n<|>y")]
    [InlineData("x\n\na<|>b", "x<|>")]
    [InlineData("a<|>b\n\ny", "<|>y")]
    [InlineData("a<|>b", "<|>")]
    [InlineData("# h<|>i\n\ny", "<|>y")]
    [InlineData("- a\n- b<|>\n- c", "- a\n- <|>c")]
    [InlineData("x\n\n> q<|>\n\ny", "x\n\n<|>y")]
    [InlineData("```\none\ntw<|>o\nthree\n```", "```\none\n<|>three\n```")]
    [InlineData("```\none\ntwo<|>\n```", "```\n<|>one\n```")]
    [InlineData("```\nx<|>\n```", "```\n<|>\n```")]
    public void CutRemovesTheBlockOrLine(string state, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state, Lines).Press("Ctrl+X").Dump());
    }

    [Fact]
    public void CutClearsATableCell()
    {
        var host = EditorTestHost.Load("| a<|> | b |\n| - | - |", Lines).Press("Ctrl+X");
        Assert.Equal(EditorTestHost.Load("| <|> | b |\n| - | - |").Dump(), host.Dump());
        Assert.Equal("a", host.Clipboard!.PlainText);
    }

    [Fact]
    public void CutThenPasteAndUndo()
    {
        var host = EditorTestHost.Load("x\n\na<|>b\n\ny", Lines).Press("Ctrl+X");
        Assert.Equal("x\n\na<|>b\n\ny", host.Press("Ctrl+Z").Dump());
        host.Press("Ctrl+X Ctrl+V");
        Assert.Equal("x\n\nab<|>y", host.Dump());
    }
}
