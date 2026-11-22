using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Ctrl+T (Typora, checked by the user): the view asks for columns and rows, then <see cref="InsertTable"/>
/// puts an empty table at the caret. An empty paragraph becomes the table; a paragraph with text is split
/// at the caret, the table between the halves. One undo item; the caret in the first header cell.
/// </summary>
public sealed class InsertTableTests
{
    private const string Empty = "| <|> |  |\n| --- | --- |\n|  |  |";

    [Theory]
    [InlineData("<|>", Empty)]
    [InlineData("ab<|>cd", "ab\n\n" + Empty + "\n\ncd")]
    [InlineData("ab<|>", "ab\n\n" + Empty)]
    [InlineData("<|>ab", Empty + "\n\nab")]
    [InlineData("x\n\n<|>\n\ny", "x\n\n" + Empty + "\n\ny")]
    [InlineData("# h<|>", "# h\n\n" + Empty)]
    [InlineData("- a<|>b", "- a\n\n  | <|> |  |\n  | --- | --- |\n  |  |  |\n\n  b")]
    [InlineData("a<^>bc<$>d", "a\n\n" + Empty + "\n\nd")]
    public void InsertsAnEmptyTableAtTheCaret(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Execute(new InsertTable(2, 2));
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("| a<|> |\n| - |")]
    [InlineData("```\nco<|>de\n```")]
    public void DoesNothingInCellsAndRawText(string state)
    {
        var host = EditorTestHost.Load(state);
        host.Execute(new InsertTable(2, 2));
        Assert.Equal(state, host.Dump());
    }

    [Fact]
    public void TheTableReopensAsATableAndUndoesInOneStep()
    {
        var host = EditorTestHost.Load("ab<|>cd");
        host.Execute(new InsertTable(3, 4));
        var reopened = MarkdownImporter.Import(MarkdownSerializer.Serialize(host.State.Document), new SequentialBlockIdSource()).Document;
        var table = Assert.IsType<Table>(reopened.Children[1]);
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(4, table.Children.Count);
        host.Press("Ctrl+Z");
        Assert.Equal("ab<|>cd", host.Dump());
    }

    [Fact]
    public void CtrlTOpensTheDialog()
    {
        Assert.Equal(new DialogBinding(EditorDialog.InsertTable), KeyMap.Default.Find(KeyChord.Parse("Ctrl+T")));
    }
}
