using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// M6.0, the Paragraph menu (Typora's shortcuts; behavior confirmed by the user): heading levels, code
/// and math blocks, quotes, lists, indentation and the Insert menu; Ctrl+D and Ctrl+Shift+D.
/// </summary>
public sealed class BlockFormatTests
{
    private static string Press(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        return host.Dump();
    }

    private static string Run(string before, Command command)
    {
        var host = EditorTestHost.Load(before);
        host.Execute(command);
        return host.Dump();
    }

    // 15: Ctrl+= raises a level, Ctrl+- lowers one.
    [Theory]
    [InlineData("a<|>b", "Ctrl+=", "###### a<|>b")]
    [InlineData("### a<|>b", "Ctrl+=", "## a<|>b")]
    [InlineData("# a<|>b", "Ctrl+=", "# a<|>b")]
    [InlineData("### a<|>b", "Ctrl+-", "#### a<|>b")]
    [InlineData("###### a<|>b", "Ctrl+-", "a<|>b")]
    [InlineData("a<|>b", "Ctrl+-", "a<|>b")]
    public void HeadingLevels(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    // 10, 11: code and math blocks.
    [Theory]
    [InlineData("a<|>b", "Ctrl+Shift+K", "```\na<|>b\n```")]
    [InlineData("<|>", "Ctrl+Shift+K", "```\n<|>\n```")]
    [InlineData("a<^>b\n\nc<$>d", "Ctrl+Shift+K", "```\na<^>b\nc<$>d\n```")]
    [InlineData("```\na<|>b\n```", "Ctrl+Shift+K", "a<|>b")]
    [InlineData("```\na\n\nb<|>c\n```", "Ctrl+Shift+K", "a\n\nb<|>c")]
    [InlineData("x<|>", "Ctrl+Shift+M", "$$\nx<|>\n$$")]
    [InlineData("$$\nx<|>\n$$", "Ctrl+Shift+M", "x<|>")]
    public void CodeAndMathBlocks(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    // 12: quotes.
    [Theory]
    [InlineData("a<|>b", "> a<|>b")]
    [InlineData("a<^>b\n\nc<$>d", "> a<^>b\n>\n> c<$>d")]
    [InlineData("> a<|>b", "a<|>b")]
    [InlineData("> > a<|>b", "> a<|>b")]
    public void Quotes(string before, string after) => Assert.Equal(after, Press(before, "Ctrl+Shift+Q"));

    // 13, 14: lists.
    [Theory]
    [InlineData("a<|>b", "Ctrl+Shift+]", "- a<|>b")]
    [InlineData("a<|>b", "Ctrl+Shift+[", "1. a<|>b")]
    [InlineData("a<|>b", "Ctrl+Shift+X", "- [ ] a<|>b")]
    [InlineData("a<^>b\n\nc<$>d", "Ctrl+Shift+]", "- a<^>b\n- c<$>d")]
    [InlineData("- a\n- b<|>\n- c", "Ctrl+Shift+]", "- a\n\nb<|>\n\n- c")]
    [InlineData("- a\n- b<|>\n- c", "Ctrl+Shift+[", "1. a\n2. b<|>\n3. c")]
    [InlineData("1. a<|>\n2. b", "Ctrl+Shift+]", "- a<|>\n- b")]
    [InlineData("- a<|>\n- b", "Ctrl+Shift+X", "- [ ] a<|>\n- [ ] b")]
    [InlineData("- [x] a<|>", "Ctrl+Shift+X", "a<|>")]
    public void Lists(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    // 16: Ctrl+[ indents list items like Tab, without inserting a tab elsewhere; Ctrl+] outdents.
    [Theory]
    [InlineData("- a\n- b<|>", "Ctrl+[", "- a\n    - b<|>")]
    [InlineData("- a\n    - b<|>", "Ctrl+]", "- a\n- b<|>")]
    [InlineData("a<|>b", "Ctrl+[", "a<|>b")]
    [InlineData("```\na<|>\n```", "Ctrl+[", "```\na<|>\n```")]
    public void Indentation(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    [Theory]
    [InlineData("a<|>b", InsertedBlock.ParagraphAbove, "<|>\n\nab")]
    [InlineData("a<|>b", InsertedBlock.ParagraphBelow, "ab\n\n<|>")]
    [InlineData("| a<|> |\n| - |", InsertedBlock.ParagraphBelow, "| a |\n| - |\n\n<|>")]
    [InlineData("a<|>b", InsertedBlock.HorizontalRule, "a\n\n---\n\n<|>b")]
    [InlineData("<|>", InsertedBlock.TableOfContents, "[toc]\n\n<|>")]
    [InlineData("a<|>", InsertedBlock.FrontMatter, "---\n<|>\n---\n\na")]
    [InlineData("---\nt: 1\n---\n\na<|>", InsertedBlock.FrontMatter, "---\nt: 1<|>\n---\n\na")]
    public void TheInsertMenu(string before, InsertedBlock block, string after) => Assert.Equal(after, Run(before, new InsertBlock(block)));

    // 17, 18: Ctrl+D selects the word, again does nothing; Ctrl+Shift+D deletes the word or the selection.
    [Theory]
    [InlineData("one tw<|>o three", "Ctrl+D", "one <^>two<$> three")]
    [InlineData("one tw<|>o three", "Ctrl+D Ctrl+D", "one <^>two<$> three")]
    [InlineData("one tw<|>o three", "Ctrl+Shift+D", "one <|> three")]
    [InlineData("o<^>ne tw<$>o", "Ctrl+Shift+D", "o<|>o")]
    public void WordKeys(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    [Fact]
    public void BlockCommandsReopenAsTheSameStructure()
    {
        var host = EditorTestHost.Load("a<^>b\n\nc<$>d");
        host.Press("Ctrl+Shift+X");
        var reopened = MarkdownImporter.Import(MarkdownSerializer.Serialize(host.State.Document), new SequentialBlockIdSource()).Document;
        var list = Assert.IsType<ListBlock>(Assert.Single(reopened.Children));
        Assert.All(list.Children.Cast<ListItem>(), item => Assert.Equal(TaskState.Unchecked, item.Task));
        host.Press("Ctrl+Z");
        Assert.Equal("a<^>b\n\nc<$>d", host.Dump());
    }
}
