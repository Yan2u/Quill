using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Paragraphs and block boundaries, 04 behavior catalog: Enter and Shift+Enter in a paragraph,
/// Backspace at the start (Q36, Q40), Delete at the end (Q36), cross-block deletion.
/// </summary>
public sealed class ParagraphBehaviorTests
{
    private static string Run(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        return host.Dump();
    }

    [Theory]
    [InlineData("a<|>b", "Enter", "a\n\n<|>b")]
    [InlineData("ab<|>", "Enter", "ab\n\n<|>")]
    [InlineData("a<|>b", "Shift+Enter", "a\n<|>b")]
    [InlineData("a<|>b", "Ctrl+Enter", "a\n\n<|>b")]
    public void EnterAndSoftBreak(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // Backspace right after a line break removes the break (user report after M5.6: soft breaks and
    // code lines; PreviousGrapheme stepped over nothing when the character before was "\n").
    [Theory]
    [InlineData("ab\n<|>", "ab<|>")]
    [InlineData("ab\n<|>cd", "ab<|>cd")]
    [InlineData("```\nab\n<|>\n```", "```\nab<|>\n```")]
    [InlineData("```\nab\n<|>cd\n```", "```\nab<|>cd\n```")]
    [InlineData("```\nab\n\n<|>\n```", "```\nab\n<|>\n```")]
    [InlineData("$$\nx\n<|>y\n$$", "$$\nx<|>y\n$$")]
    public void BackspaceAfterALineBreakRemovesIt(string before, string after) => Assert.Equal(after, Run(before, "Backspace"));

    // An empty code block is deleted by Backspace or Delete (Typora, checked by the user): it becomes an
    // empty paragraph where it was. Math the same; a code block with text keeps the old rule.
    [Theory]
    [InlineData("a\n\n```\n<|>\n```\n\nb", "Backspace", "a\n\n<|>\n\nb")]
    [InlineData("a\n\n```js\n<|>\n```\n\nb", "Delete", "a\n\n<|>\n\nb")]
    [InlineData("```\n<|>\n```", "Backspace", "<|>")]
    [InlineData("- ```\n  <|>\n  ```", "Backspace", "- <|>")]
    [InlineData("a\n\n$$\n<|>\n$$", "Backspace", "a\n\n<|>")]
    [InlineData("a\n\n```\n<|>x\n```", "Backspace", "a\n\n```\n<|>x\n```")]
    public void BackspaceOrDeleteInAnEmptyCodeBlockDeletesIt(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // Backspace at the start merges into the last text leaf of the block above (Q40).
    [Theory]
    [InlineData("a\n\n<|>b", "a<|>b")]
    [InlineData("# a\n\n<|>b", "# a<|>b")]
    [InlineData("> a\n\n<|>b", "> a<|>b")]
    [InlineData("- a\n- c\n\n<|>b", "- a\n- c<|>b")]
    [InlineData("```\ncode\n```\n\n<|>b", "```\ncode<|>b\n```")]
    [InlineData("$$\nx\n$$\n\n<|>b", "$$\nx<|>b\n$$")]
    [InlineData("| x | y |\n| - | - |\n| 1 | 2 |\n\n<|>b", "| x | y |\n| - | - |\n| 1 | 2<|>b |")]
    [InlineData("| x |\n| - |\n\n<|>a|b", "| x<|>a\\|b |\n| - |")]
    [InlineData("[^1]: note\n\n<|>b", "[^1]: note<|>b")]
    [InlineData("[a]: http://x \"title\"\n\n<|>b", "[a]: http://x \"title<|>b\"")]
    public void BackspaceAtStartMergesIntoPreviousTextLeaf(string before, string after) => Assert.Equal(after, Run(before, "Backspace"));

    // A block above without text is selected whole first; the next Backspace deletes it (Q36).
    [Theory]
    [InlineData("---\n\n<|>b", "<block>\n\n---\n\nb", "<|>b")]
    [InlineData("a\n\n[toc]\n\n<|>b", "a\n\n<block>\n\n[toc]\n\nb", "a<|>\n\nb")]
    public void BackspaceAfterBlockWithoutTextSelectsItFirst(string before, string selected, string deleted)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Backspace");
        Assert.Equal(selected, host.Dump());
        host.Press("Backspace");
        Assert.Equal(deleted, host.Dump());
    }

    [Fact]
    public void BackspaceAtDocumentStartDoesNothing() => Assert.Equal("<|>a", Run("<|>a", "Backspace"));

    // Delete at the end: a paragraph or heading below merges up; anything else is selected whole (Q36, Q40).
    [Theory]
    [InlineData("a<|>\n\nb", "a<|>b")]
    [InlineData("a<|>\n\n# b", "a<|>b")]
    [InlineData("a<|>\n\n---", "a\n\n<block>\n\n---")]
    [InlineData("a<|>\n\n> b", "a\n\n<gap:^>\n\n> b\n\n<gap:$>")]
    [InlineData("a<|>\n\n- b", "a\n\n<gap:^>\n\n- b\n\n<gap:$>")]
    [InlineData("a<|>\n\n```\nc\n```", "a\n\n<gap:^>\n\n```\nc\n```\n\n<gap:$>")]
    [InlineData("> a<|>\n\nb", "> a<|>b")]
    public void DeleteAtEnd(string before, string after) => Assert.Equal(after, Run(before, "Delete"));

    [Fact]
    public void DeleteAfterSelectingBlockRemovesIt()
    {
        var host = EditorTestHost.Load("a<|>\n\n---\n\nb");
        host.Press("Delete Delete");
        Assert.Equal("a<|>\n\nb", host.Dump());
    }

    // Cross-block selections: the outer halves stay and merge; code blocks go whole or not at all.
    [Theory]
    [InlineData("a<^>b\n\nc<$>d", "Backspace", "a<|>d")]
    [InlineData("a<$>b\n\nc<^>d", "Delete", "a<|>d")]
    [InlineData("# a<^>b\n\nc<$>d", "Backspace", "# a<|>d")]
    [InlineData("a<^>b\n\n```\nco<$>de\n```\n\nz", "Backspace", "a<|>\n\nz")]
    [InlineData("- a<^>b\n- c\n\nd<$>e", "Backspace", "- a<|>e")]
    [InlineData("a<^>b\n\nc<$>d", "x", "ax<|>d")]
    public void CrossBlockSelection(string before, string keys, string after)
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

    // Deleting across cells only clears text; the table keeps its structure.
    [Fact]
    public void CrossCellDeletionClearsText()
    {
        var host = EditorTestHost.Load("| <^>a | b<$>c |\n| - | - |\n| 1 | 2 |");
        host.Press("Delete");
        Assert.Equal("| <|> | c |\n| - | - |\n| 1 | 2 |", host.Dump());
    }

    [Fact]
    public void CodeBlockIsSelectedWholeWhenSelectionLeavesIt()
    {
        var host = EditorTestHost.Load("a<^>b\n\n```\nco<$>de\n```");
        Assert.Equal("a<^>b\n\n```\ncode\n```\n\n<gap:$>", host.Dump());
    }
}
