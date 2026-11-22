using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Element behaviors of the 04 catalog other than lists and paragraphs: quotes, code blocks, tables,
/// definitions (10-elements.md, Q35) and virtual paragraphs (Q19).
/// </summary>
public sealed class ElementBehaviorTests
{
    private static string Run(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        return host.Dump();
    }

    // Quote: Backspace at a paragraph start merges with the text leaf before it at the same level,
    // otherwise removes one level of quote.
    [Theory]
    [InlineData("> a\n>\n> <|>b", "> a<|>b")]
    [InlineData("> <|>a", "<|>a")]
    [InlineData("> > <|>a", "> <|>a")]
    [InlineData("> a\n>\n> > <|>b", "> a\n>\n> <|>b")]
    [InlineData("> ```\n> c\n> ```\n>\n> <|>a", "> ```\n> c\n> ```\n\n<|>a")]
    [InlineData("> <|>a\n>\n> b", "<|>a\n\n> b")]
    public void BackspaceInQuote(string before, string after) => Assert.Equal(after, Run(before, "Backspace"));

    // Quote: Enter in an empty paragraph leaves the quote, splitting it when in the middle.
    [Theory]
    [InlineData("> a\n>\n> <|>", "> a\n\n<|>")]
    [InlineData("> a\n>\n> <|>\n>\n> b", "> a\n\n<|>\n\n> b")]
    [InlineData("> > a\n> >\n> > <|>", "> > a\n>\n> <|>")]
    [InlineData("> a<|>b", "> a\n>\n> <|>b")]
    public void EnterInQuote(string before, string after) => Assert.Equal(after, Run(before, "Enter"));

    // Code blocks: Enter and Shift+Enter insert a line break; Ctrl+Enter at the very end leaves into a
    // real paragraph, elsewhere it does nothing (Q19); Tab types a tab.
    [Theory]
    [InlineData("```\na<|>b\n```", "Enter", "```\na\n<|>b\n```")]
    [InlineData("```\na<|>b\n```", "Shift+Enter", "```\na\n<|>b\n```")]
    [InlineData("```\nab<|>\n```", "Ctrl+Enter", "```\nab\n```\n\n<|>")]
    [InlineData("```\na<|>b\n```", "Ctrl+Enter", "```\na<|>b\n```")]
    [InlineData("```\na<|>b\n```", "Tab", "```\na\t<|>b\n```")]
    [InlineData("```\n<|>ab\n```", "Backspace", "```\n<|>ab\n```")]
    [InlineData("$$\nx<|>\n$$", "Enter", "$$\nx\n<|>\n$$")]
    public void CodeAndMathBlocks(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // Table cells: Enter moves to the same column of the next row, then out of the table (Q19).
    [Theory]
    [InlineData("| a | b |\n| - | - |\n| 1<|> | 2 |\n| 3 | 4 |", "| a | b |\n| - | - |\n| 1 | 2 |\n| <|>3 | 4 |")]
    [InlineData("| a | b<|> |\n| - | - |\n| 1 | 2 |", "| a | b |\n| - | - |\n| 1 | <|>2 |")]
    [InlineData("| a |\n| - |\n| 1<|> |", "| a |\n| - |\n| 1 |\n\n<|>")]
    [InlineData("| a |\n| - |\n| 1<|> |\n\nnext", "| a |\n| - |\n| 1 |\n\n<|>next")]
    [InlineData("| a |\n| - |\n| 1<|> |\n\n```\nc\n```", "| a |\n| - |\n| 1 |\n\n<|>\n\n```\nc\n```")]
    public void EnterInTableCell(string before, string after) => Assert.Equal(after, Run(before, "Enter"));

    [Theory]
    [InlineData("| a<|> | b |\n| - | - |", "Tab", "| a | <|>b |\n| - | - |")]
    [InlineData("| a | b<|> |\n| - | - |\n| 1 | 2 |", "Tab", "| a | b |\n| - | - |\n| <|>1 | 2 |")]
    [InlineData("| a | <|>b |\n| - | - |", "Shift+Tab", "| <|>a | b |\n| - | - |")]
    [InlineData("| <|>a | b |\n| - | - |", "Shift+Tab", "| <|>a | b |\n| - | - |")]
    [InlineData("| a<|> |\n| - |", "Shift+Enter", "| a<br><|> |\n| - |")]
    [InlineData("| <|>a |\n| - |", "Backspace", "| <|>a |\n| - |")]
    [InlineData("| a<|> | b |\n| - | - |", "Delete", "| a<|> | b |\n| - | - |")]
    public void TableCellKeys(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    [Fact]
    public void TabAfterLastCellAddsRow()
    {
        var host = EditorTestHost.Load("| a | b |\n| - | - |\n| 1 | 2<|> |");
        host.Press("Tab");
        var table = TestMarkdown.Single<Documents.Table>(host.State.Document);
        Assert.Equal(3, table.Children.Count);
        var row = Assert.IsType<Documents.TableRow>(table.Children[2]);
        Assert.Equal(2, row.Children.Count);
        Assert.Equal(row.Children[0].Id, Assert.IsType<State.TextPosition>(host.State.Selection.Head).Block);
    }

    [Fact]
    public void PipeTypedInCellIsEscaped()
    {
        var host = EditorTestHost.Load("| a<|> |\n| - |");
        host.Type("|b");
        Assert.Equal("| a\\|b<|> |\n| - |", host.Dump());
    }

    // A pipe whose backslash is deleted is escaped again: a cell cannot hold a bare pipe (Q40 default).
    [Theory]
    [InlineData("| a<|>\\|b |\n| - |", "Delete", "| a\\<|>|b |\n| - |")]
    [InlineData("| a\\\\<|> |\n| - |", "|", "| a\\\\\\|<|> |\n| - |")]
    public void CellPipesStayEscaped(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        if (keys.Length == 1)
        {
            host.Type(keys);
        }
        else
        {
            host.Press(keys);
        }

        Assert.Equal(after, host.Dump());
    }

    // Tab order (04): in a table inside a list item, the cell moves on before the item could indent.
    [Fact]
    public void TabInTableInsideListItemMovesToNextCell()
    {
        var host = EditorTestHost.Load("- a\n- b\n\n  | x | y |\n  | - | - |\n  | 1<|> | 2 |");
        host.Press("Tab");
        Assert.Equal("- a\n- b\n\n  | x | y |\n  | - | - |\n  | 1 | <|>2 |", host.Dump());
    }

    // Definitions (Q35): Backspace and Delete move between slots; Backspace in the leftmost slot
    // turns the definition into a paragraph; Enter splits the definition.
    [Theory]
    [InlineData("[^1]: <|>note", "Backspace", "[^1<|>]: note")]
    [InlineData("[^<|>1]: note", "Backspace", "<|>note")]
    [InlineData("[^1<|>]: note", "Delete", "[^1]: <|>note")]
    [InlineData("[^1]: note<|>", "Delete", "[^1]: note<|>")]
    [InlineData("[a]: <|>http://x \"t\"", "Backspace", "[a<|>]: http://x \"t\"")]
    [InlineData("[<|>a]: http://x \"t\"", "Backspace", "<|>http://x")]
    [InlineData("[a]: http://x<|> \"t\"", "Delete", "[a]: http://x \"<|>t\"")]
    [InlineData("[a]: http://x \"t<|>\"", "Delete", "[a]: http://x \"t<|>\"")]
    public void DefinitionSlotKeys(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // A title that comes to contain its own delimiter is written with another one.
    [Theory]
    [InlineData("[a]: http://x \"t<|>\"", "\"", "[a]: http://x 't\"<|>'")]
    [InlineData("[a]: http://x 't<|>'", "'", "[a]: http://x \"t'<|>\"")]
    public void LinkTitleDelimiterAvoidsTitleText(string before, string typed, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Type(typed);
        Assert.Equal(after, host.Dump());
        var reopened = TestMarkdown.Import(TestMarkdown.Serialize(host.State.Document));
        Assert.Equal("t" + typed, TestMarkdown.Single<Documents.LinkReferenceDefinition>(reopened).Title.Text);
    }

    [Fact]
    public void EnterSplitsFootnoteDefinition()
    {
        var host = EditorTestHost.Load("[^1]: no<|>te");
        host.Press("Enter");
        var document = host.State.Document;
        var first = Assert.IsType<Documents.FootnoteDefinition>(document.Children[0]);
        var second = Assert.IsType<Documents.FootnoteDefinition>(document.Children[1]);
        Assert.Equal(("1", "no"), (first.Name.Text, first.Content.Text));
        Assert.Equal((string.Empty, "te"), (second.Name.Text, second.Content.Text));
        Assert.Equal(State.TextSelection.Caret(second.Content.Id, 0), host.State.Selection);
    }

    [Fact]
    public void EnterSplitsLinkDefinitionCarryingLaterSlots()
    {
        var host = EditorTestHost.Load("[a]: http://<|>x \"t\"");
        host.Press("Enter");
        var document = host.State.Document;
        var first = Assert.IsType<Documents.LinkReferenceDefinition>(document.Children[0]);
        var second = Assert.IsType<Documents.LinkReferenceDefinition>(document.Children[1]);
        Assert.Equal(("a", "http://", string.Empty), (first.Label.Text, first.Destination.Text, first.Title.Text));
        Assert.Equal((string.Empty, "x", "t"), (second.Label.Text, second.Destination.Text, second.Title.Text));
        Assert.Equal(State.TextSelection.Caret(second.Destination.Id, 0), host.State.Selection);
    }

    // An empty footnote definition is written "[^]:", which Typora and Quill both read back.
    [Fact]
    public void EmptyFootnoteDefinitionSurvivesSaving()
    {
        var host = EditorTestHost.Load("[^1]: note<|>");
        host.Press("Enter");
        string saved = TestMarkdown.Serialize(host.State.Document);
        Assert.Equal("[^1]: note\n\n[^]: ", saved);
        var reopened = TestMarkdown.Import(saved);
        var second = Assert.IsType<Documents.FootnoteDefinition>(reopened.Children[1]);
        Assert.Equal((string.Empty, string.Empty), (second.Name.Text, second.Content.Text));
    }

    // A link definition needs a label: an empty one is written with a placeholder no other definition
    // or text uses, and an empty destination as "<>".
    [Theory]
    [InlineData("[a]: http://x<|>", "[a]: http://x\n\n[ref1]: <>")]
    [InlineData("[a]: http://<|>x \"t\"", "[a]: http://\n\n[ref1]: x \"t\"")]
    [InlineData("[ref1]: http://y\n\n[a]: http://x<|>", "[ref1]: http://y\n\n[a]: http://x\n\n[ref2]: <>")]
    [InlineData("See [REF1].\n\n[a]: http://x<|>", "See [REF1].\n\n[a]: http://x\n\n[ref2]: <>")]
    public void EmptyLinkLabelIsWrittenWithAPlaceholder(string before, string saved)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Enter");
        Assert.Equal(saved, TestMarkdown.Serialize(host.State.Document));
        var reopened = TestMarkdown.Import(saved);
        Assert.Equal(host.State.Document.Children.Count, reopened.Children.Count);
        Assert.IsType<Documents.LinkReferenceDefinition>(reopened.Children[^1]);
    }

    // Virtual paragraphs: typing makes them real; Backspace and Delete reach the blocks around them.
    [Theory]
    [InlineData("```\na\n```\n\n<gap>\n\n---", "x", "```\na\n```\n\nx<|>\n\n---")]
    [InlineData("```\na\n```\n\n<gap>", "x", "```\na\n```\n\nx<|>")]
    [InlineData("<gap>\n\n---", "x", "x<|>\n\n---")]
    public void TypingInVirtualParagraph(string before, string text, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Type(text);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("```\na\n```\n\n<gap>\n\n---", "Backspace", "```\na<|>\n```\n\n---")]
    [InlineData("***\n\n<gap>\n\n***", "Backspace", "<block>\n\n***\n\n***")]
    [InlineData("```\na\n```\n\n<gap>\n\n---", "Delete", "```\na\n```\n\n<block>\n\n---")]
    [InlineData("```\na\n```\n\n<gap>\n\n---", "Enter", "```\na\n```\n\n<empty>\n\n<|>\n\n---")]
    public void KeysInVirtualParagraph(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    [Fact]
    public void LeavingVirtualParagraphLeavesNothing()
    {
        var host = EditorTestHost.Load("```\na\n```\n\n<gap>\n\n---");
        host.Press("Up");
        Assert.Equal(2, host.State.Document.Children.Count);
        Assert.False(host.Session.History.CanUndo);
    }
}
