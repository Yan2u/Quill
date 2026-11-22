using Quill.Editor.Commands;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// M6.0, the Format menu (Typora's shortcuts; behavior confirmed by the user): inline formats toggled
/// on a caret, a selection or a selection across blocks; links, images and clearing formats.
/// </summary>
public sealed class InlineFormatTests
{
    private static string Run(string before, Command command)
    {
        var host = EditorTestHost.Load(before);
        host.Execute(command);
        return host.Dump();
    }

    private static string Press(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        return host.Dump();
    }

    // 1: a caret alone inserts both markers, the caret between them; typing then is bold.
    [Fact]
    public void ACaretInsertsTheMarkersAndTypingIsFormatted()
    {
        var host = EditorTestHost.Load("ab<|>cd");
        host.Press("Ctrl+B").Type("x");
        Assert.Equal("ab**x<|>**cd", host.Dump());
    }

    [Theory]
    [InlineData("a**b<|>c**d", "Ctrl+B", "ab<|>cd")]                    // 2: caret in bold removes it
    [InlineData("a**<^>bc<$>**d", "Ctrl+B", "a<^>bc<$>d")]              // 3: exactly the bold text
    [InlineData("a**b<^>c<$>d**e", "Ctrl+B", "ab<^>c<$>de")]            // inside bold: removes the element
    [InlineData("<^>ab<$> cd", "Ctrl+B", "**<^>ab<$>** cd")]
    [InlineData("x<^> ab <$>y", "Ctrl+B", "x **<^>ab<$>** y")]          // spaces stay outside the markers
    [InlineData("a<^>b **c<$>d** e", "Ctrl+B", "a**<^>b cd<$>** e")]    // 4: merges with the bold it overlaps
    [InlineData("<^>ab<$>", "Ctrl+I", "*<^>ab<$>*")]
    [InlineData("<^>ab<$>", "Ctrl+U", "<u><^>ab<$></u>")]               // 6
    [InlineData("<^>ab<$>", "Alt+Shift+5", "~~<^>ab<$>~~")]
    [InlineData("<^>a`b<$>", "Ctrl+Shift+`", "``<^>a`b<$>``")]           // 6: a longer run of backticks
    [InlineData("a<u>b<|>c</u>d", "Ctrl+U", "ab<|>cd")]
    [InlineData("a`b<|>c`d", "Ctrl+Shift+`", "ab<|>cd")]
    public void TogglesTheFormat(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    [Theory]
    [InlineData("<^>ab<$>", InlineFormat.Math, "$<^>ab<$>$")]
    [InlineData("<^>ab<$>", InlineFormat.Superscript, "^<^>ab<$>^")]
    [InlineData("<^>ab<$>", InlineFormat.Subscript, "~<^>ab<$>~")]
    [InlineData("<^>ab<$>", InlineFormat.Comment, "<!--<^>ab<$>-->")]
    [InlineData("x <!--<^>ab<$>--> y", InlineFormat.Comment, "x <^>ab<$> y")]
    public void TheMenuOnlyFormats(string before, InlineFormat format, string after) => Assert.Equal(after, Run(before, new ToggleInline(format)));

    // 5: across blocks, each block's part is formatted.
    [Fact]
    public void ASelectionAcrossBlocksFormatsEachPart()
    {
        Assert.Equal("a**<^>b**\n\n**c<$>**d", Press("a<^>b\n\nc<$>d", "Ctrl+B"));
    }

    [Fact]
    public void FormattingIsOneUndoItem()
    {
        var host = EditorTestHost.Load("a<^>b\n\nc<$>d");
        host.Press("Ctrl+B Ctrl+Z");
        Assert.Equal("a<^>b\n\nc<$>d", host.Dump());
    }

    [Theory]
    [InlineData("| a<|> |\n| - |", "| a**<|>** |\n| - |")]
    [InlineData("```\na<|>\n```", "```\na<|>\n```")]
    public void CellsTakeFormatsRawTextDoesNot(string before, string after) => Assert.Equal(after, Press(before, "Ctrl+B"));

    // 7: Ctrl+K (the view supplies a URL from the clipboard, or none).
    [Theory]
    [InlineData("a<^>bc<$>d", null, "a[bc](<|>)d")]
    [InlineData("a<|>d", null, "a[<|>]()d")]
    [InlineData("a<^>bc<$>d", "https://x.org", "a[bc](https://x.org<|>)d")]
    [InlineData("a[b<|>c](u)d", null, "ab<|>cd")]
    public void CtrlKMakesOrRemovesALink(string before, string? url, string after) => Assert.Equal(after, Run(before, new InsertLink(url)));

    // 8: Ctrl+Shift+I.
    [Theory]
    [InlineData("a<^>pic<$>d", null, "a![pic](<|>)d")]
    [InlineData("a<|>d", null, "a![<|>]()d")]
    public void CtrlShiftIInsertsAnImage(string before, string? url, string after) => Assert.Equal(after, Run(before, new InsertImage(url)));

    // 9: Ctrl+\.
    [Theory]
    [InlineData("<^>a **b** *c* `d`<$>", "<^>a b c d<$>")]
    [InlineData("a **b<|>c** *d*", "a b<|>c *d*")]
    [InlineData("<^>[a](u) **b**<$>", "<^>[a](u) b<$>")]
    public void CtrlBackslashClearsFormats(string before, string after) => Assert.Equal(after, Press(before, "Ctrl+\\"));
}
