using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Q39 (Typora, checked by the user): Ctrl+E selects the cell, Ctrl+L the row, Ctrl+A the whole table
/// (the user's choice; Typora selects the cell). The selections are plain text selections, so typing
/// clears the selected cells and types into the first. Copying rows or a table gives their Markdown
/// as plain text; one row gives its cells' source joined by tabs. Outside tables, Ctrl+E selects the
/// styled element and Ctrl+L the line〔默认〕.
/// </summary>
public sealed class ScopeSelectionTests
{
    private const string Table = "| a | **b** | c |\n| - | - | - |\n| d | e | f |";

    [Theory]
    [InlineData("| a | **b<|>** | c |\n| - | - | - |\n| d | e | f |", "Ctrl+E", "| a | <^>**b**<$> | c |\n| - | - | - |\n| d | e | f |")]
    [InlineData("| a | **b** | c |\n| - | - | - |\n| d | e<|> | f |", "Ctrl+L", "| a | **b** | c |\n| - | - | - |\n| <^>d | e | f<$> |")]
    [InlineData("| a | **b** | c |\n| - | - | - |\n| d | e<|> | f |", "Ctrl+A", "| <^>a | **b** | c |\n| - | - | - |\n| d | e | f<$> |")]
    [InlineData("x\n\n| a |\n| - |\n| b<|> |", "Ctrl+A Ctrl+A", "<^>x\n\n| a |\n| - |\n| b<$> |")]
    [InlineData("x\n\n```cs\nint a;\nint <|>b;\n```\n\ny", "Ctrl+A", "x\n\n```cs\n<^>int a;\nint b;<$>\n```\n\ny")]
    [InlineData("x\n\n```cs\nint a;\nint <|>b;\n```\n\ny", "Ctrl+A Ctrl+A", "<^>x\n\n```cs\nint a;\nint b;\n```\n\ny<$>")]
    [InlineData("x\n\n```mermaid\nflowchart TD\n  A -<|>-> B\n```", "Ctrl+A", "x\n\n```mermaid\n<^>flowchart TD\n  A --> B<$>\n```")]
    [InlineData("x\n\n$$\na^<|>2\n$$\n\ny", "Ctrl+A", "x\n\n$$\n<^>a^2<$>\n$$\n\ny")]
    [InlineData("x\n\n$$\na^<|>2\n$$\n\ny", "Ctrl+A Ctrl+A", "<^>x\n\n$$\na^2\n$$\n\ny<$>")]
    [InlineData("---\nti<|>tle: a\n---\n\ny", "Ctrl+A", "---\n<^>title: a<$>\n---\n\ny")]
    [InlineData("x\n\n```\n<|>\n```\n\ny", "Ctrl+A", "<^>x\n\n```\n\n```\n\ny<$>")]
    [InlineData("a<|>b\n\n```\nc\n```", "Ctrl+A", "<^>ab\n\n```\nc\n```\n\n<gap:$>")]
    public void TableScopes(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void TypingOverARowClearsItsCellsAndTypesIntoTheFirst()
    {
        var host = EditorTestHost.Load("| a | **b** | c |\n| - | - | - |\n| d | e<|> | f |");
        host.Press("Ctrl+L").Type("x");
        Assert.Equal("| a | **b** | c |\n| - | - | - |\n| x<|> |  |  |", host.Dump());
    }

    [Fact]
    public void CopyingARowGivesItsCellsSourceJoinedByTabs()
    {
        // Q39 applies to the text as shown (Q102: with "copy Markdown source" off).
        var host = EditorTestHost.Load("| a | **b<|>** | c |\n| - | - | - |\n| d | e | f |", EditorOptions.Default with { CopyMarkdownSource = false });
        host.Press("Ctrl+L Ctrl+C");
        Assert.Equal("a\t**b**\tc", host.Clipboard!.PlainText);
        Assert.StartsWith("| a | **b** | c |", host.Clipboard.Markdown);
    }

    [Fact]
    public void CopyingTheTableGivesItsMarkdownAsPlainText()
    {
        var host = EditorTestHost.Load("| a | **b<|>** | c |\n| - | - | - |\n| d | e | f |");
        host.Press("Ctrl+A Ctrl+C");
        Assert.Equal(Table, host.Clipboard!.PlainText);
        Assert.Equal(Table, host.Clipboard.Markdown!.TrimEnd('\n'));
    }

    [Fact]
    public void CopyingRowsAcrossTheTableGivesMarkdownWithTheHeaderFirst()
    {
        var host = EditorTestHost.Load("| a | b |\n| - | - |\n| c | d |\n| e | f |");
        var table = (Table)host.State.Document.Children[0];
        var first = ((TableRow)table.Children[1]).Children[0];
        var last = ((TableRow)table.Children[2]).Children[1];
        host.Execute(new SetSelection(new TextSelection(new TextPosition(first.Id, 0), new TextPosition(last.Id, 1))));
        host.Press("Ctrl+C");
        Assert.Equal("| a | b |\n| - | - |\n| c | d |\n| e | f |", host.Clipboard!.PlainText);
    }

    [Theory]
    [InlineData("a **bo<|>ld** c", "Ctrl+E", "a **<^>bold<$>** c")]
    [InlineData("a **bo<|>ld** c", "Ctrl+E Ctrl+E", "a <^>**bold**<$> c")]
    [InlineData("a ***x<|>y*** c", "Ctrl+E", "a ***<^>xy<$>*** c")]
    [InlineData("plain wo<|>rd here", "Ctrl+E", "plain <^>word<$> here")]
    [InlineData("plain wo<|>rd here", "Ctrl+E Ctrl+E", "<^>plain word here<$>")]
    public void CtrlESelectsTheStyledScope(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void CtrlLSelectsTheLine()
    {
        var host = EditorTestHost.Load("one\ntw<|>o\nthree");
        host.Press("Ctrl+L");
        Assert.Equal("one\n<^>two<$>\nthree", host.Dump());
    }
}
