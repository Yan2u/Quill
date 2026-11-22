using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Table structure edits (Q76–Q78, 04): the toolbar's and its menu's operations at the caret's cell,
/// their shortcuts, one undo item each, and no effect outside a table.
/// </summary>
public sealed class TableEditTests
{
    private const string Table = "| a | b |\n| --- | --- |\n| c<|> | d |";

    [Theory]
    [InlineData("Ctrl+Enter", "| a | b |\n| --- | --- |\n| c | d |\n| <|> |  |")]
    [InlineData("Alt+Up", "| c<|> | d |\n| --- | --- |\n| a | b |")]
    [InlineData("Alt+Down", Table)]
    [InlineData("Alt+Right", "| b | a |\n| --- | --- |\n| d | c<|> |")]
    [InlineData("Alt+Left", Table)]
    [InlineData("Ctrl+Shift+Backspace", "| <|>a | b |\n| --- | --- |")]
    public void ShortcutsEditTheTable(string keys, string after)
    {
        var host = EditorTestHost.Load(Table);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
        if (after != Table)
        {
            host.Press("Ctrl+Z");
            Assert.Equal(Table, host.Dump());
        }
    }

    [Theory]
    [InlineData(TableEdit.InsertRowAbove, "| a | b |\n| --- | --- |\n| <|> |  |\n| c | d |")]
    [InlineData(TableEdit.InsertColumnLeft, "|  | a | b |\n| --- | --- | --- |\n| <|> | c | d |")]
    [InlineData(TableEdit.InsertColumnRight, "| a |  | b |\n| --- | --- | --- |\n| c | <|> | d |")]
    [InlineData(TableEdit.DeleteColumn, "| b |\n| --- |\n| <|>d |")]
    [InlineData(TableEdit.DeleteTable, "<|>")]
    public void MenuEditsTheTable(TableEdit edit, string after)
    {
        var host = EditorTestHost.Load(Table);
        host.Execute(new EditTable(edit));
        Assert.Equal(after, host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal(Table, host.Dump());
    }

    [Fact]
    public void DeletingTheLastRowOrColumnDeletesTheTable()
    {
        var host = EditorTestHost.Load("before\n\n| a<|> |\n| --- |\n\nafter");
        host.Execute(new EditTable(TableEdit.DeleteRow));
        Assert.Equal("before\n\n<|>after", host.Dump());

        host = EditorTestHost.Load("before\n\n| a<|> | b |\n| --- | --- |");
        host.Execute(new EditTable(TableEdit.DeleteColumn));
        host.Execute(new EditTable(TableEdit.DeleteColumn));
        Assert.Equal("before<|>", host.Dump());
    }

    [Fact]
    public void AlignmentSetsTheColumnAndTheSameAgainResetsIt()
    {
        var host = EditorTestHost.Load(Table);
        host.Execute(new SetColumnAlignment(TableAlignment.Center));
        Assert.Equal("| a | b |\n| :---: | --- |\n| c<|> | d |", host.Dump());
        host.Execute(new SetColumnAlignment(TableAlignment.Right));
        Assert.Equal("| a | b |\n| ---: | --- |\n| c<|> | d |", host.Dump());
        host.Execute(new SetColumnAlignment(TableAlignment.Right));
        Assert.Equal("| a | b |\n| --- | --- |\n| c<|> | d |", host.Dump());
    }

    [Fact]
    public void ResizeGrowsAndCutsWithContent()
    {
        var host = EditorTestHost.Load(Table);
        host.Execute(new ResizeTable(3, 3));
        Assert.Equal("| a | b |  |\n| --- | --- | --- |\n| c<|> | d |  |\n|  |  |  |", host.Dump());
        host.Execute(new ResizeTable(1, 1));
        Assert.Equal("| <|>a |\n| --- |", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("| a | b |  |\n| --- | --- | --- |\n| c<|> | d |  |\n|  |  |  |", host.Dump());
    }

    [Fact]
    public void FormattingPadsTheSourceOnly()
    {
        var host = EditorTestHost.Load("| a | bbbb |\n|:-|-:|\n| 中文<|> | d |");
        host.Execute(new EditTable(TableEdit.FormatSource));
        Assert.Equal("| a    | bbbb |\n| :--- | ---: |\n| 中文<|> | d    |", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("| a | bbbb |\n|:-|-:|\n| 中文<|> | d |", host.Dump());
    }

    [Fact]
    public void CopyTableGivesItsMarkdown()
    {
        var host = EditorTestHost.Load("text\n\n" + Table);
        var copied = host.Session.CopyTable();
        Assert.NotNull(copied);
        Assert.Equal("| a | b |\n| --- | --- |\n| c | d |", copied!.Markdown!.TrimEnd('\n'));
        Assert.Equal(copied.Markdown, copied.PlainText);
    }

    [Theory]
    [InlineData("Alt+Up")]
    [InlineData("Alt+Right")]
    [InlineData("Ctrl+Shift+Backspace")]
    public void OutsideATableTheShortcutsDoNothing(string keys)
    {
        var host = EditorTestHost.Load("one\n\ntw<|>o");
        host.Press(keys);
        Assert.Equal("one\n\ntw<|>o", host.Dump());
    }
}
