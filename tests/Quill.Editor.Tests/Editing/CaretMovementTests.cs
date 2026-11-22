using Quill.Editor.Commands;
using Quill.Editor.Input;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Caret movement (04): grapheme and word steps across blocks, virtual paragraph stops, collapsing a
/// selection, line ends and vertical moves through the fake layout with a goal X (08).
/// </summary>
public sealed class CaretMovementTests
{
    private static string Run(string before, string keys, int columns = 80)
    {
        var host = EditorTestHost.Load(before, columns: columns);
        host.Press(keys);
        return host.Dump();
    }

    [Theory]
    [InlineData("a<|>bc", "Right", "ab<|>c")]
    [InlineData("a<|>\n\nb", "Right", "a\n\n<|>b")]
    [InlineData("a\n\n<|>b", "Left", "a<|>\n\nb")]
    [InlineData("a<|>\n\n---\n\nb", "Right", "a\n\n---\n\n<|>b")]
    [InlineData("```\na<|>\n```\n\n---", "Right", "```<|>\na\n```\n\n---")]
    [InlineData("```<|>\na\n```\n\n---", "Right", "```\na\n```\n\n<gap>\n\n---")]
    [InlineData("```\na\n```\n\n<gap>\n\n---", "Left", "```<|>\na\n```\n\n---")]
    [InlineData("- a<|>\n- b", "Right", "- a\n- <|>b")]
    [InlineData("| a<|> | b |\n| - | - |", "Right", "| a | <|>b |\n| - | - |")]
    [InlineData("á<|>b", "Left", "<|>áb")]
    [InlineData("a👍🏽<|>b", "Left", "a<|>👍🏽b")]
    [InlineData("<|>​\tab", "Right", "​\t<|>ab")]
    [InlineData("​\t<|>ab", "Left", "<|>​\tab")]
    [InlineData("a<^>bc<$>d", "Left", "a<|>bcd")]
    [InlineData("a<^>bc<$>d", "Right", "abc<|>d")]
    [InlineData("a<|>bc", "Shift+Right Shift+Right", "a<^>bc<$>")]
    [InlineData("hello world<|>", "Ctrl+Left", "hello <|>world")]
    [InlineData("<|>hello world", "Ctrl+Right", "hello<|> world")]
    [InlineData("a\n\nb<|>", "Ctrl+Home", "<|>a\n\nb")]
    [InlineData("<|>a\n\nb", "Ctrl+End", "a\n\nb<|>")]
    [InlineData("<|>a\n\nb", "Ctrl+Shift+End", "<^>a\n\nb<$>")]
    public void HorizontalMoves(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    [Theory]
    [InlineData("abc<|>", "Home", "<|>abc")]
    [InlineData("<|>abc", "End", "abc<|>")]
    [InlineData("abcdefgh<|>", "Home", "abcd<|>efgh", 4)]
    [InlineData("ab<|>cdefgh", "End", "abcd<|>efgh", 4)]
    public void LineEnds(string before, string keys, string after, int columns = 80) => Assert.Equal(after, Run(before, keys, columns));

    [Fact]
    public void VerticalMovesKeepGoalX()
    {
        var host = EditorTestHost.Load("abcde<|>f\n\nab\n\nabcdef");
        host.Press("Down");
        Assert.Equal("abcdef\n\nab<|>\n\nabcdef", host.Dump());
        host.Press("Down");
        Assert.Equal("abcdef\n\nab\n\nabcde<|>f", host.Dump());
        host.Press("Up Up");
        Assert.Equal("abcde<|>f\n\nab\n\nabcdef", host.Dump());
    }

    [Fact]
    public void VerticalMovesFollowWrappedLines()
    {
        var host = EditorTestHost.Load("a<|>bcdefgh", columns: 4);
        host.Press("Down");
        Assert.Equal("abcde<|>fgh", host.Dump());
    }

    [Fact]
    public void UpOnFirstLineGoesToDocumentStart()
    {
        Assert.Equal("<|>abc\n\nd", Run("ab<|>c\n\nd", "Up"));
        Assert.Equal("abc\n\nd<|>", Run("abc\n\n<|>d", "Down"));
    }

    [Fact]
    public void OtherCommandsClearGoalX()
    {
        var host = EditorTestHost.Load("abcde<|>f\n\nab");
        host.Press("Down");
        Assert.NotNull(host.State.GoalX);
        host.Press("Left");
        Assert.Null(host.State.GoalX);
    }
}

/// <summary>The key map is data with platform-independent chords (07).</summary>
public sealed class KeyMapTests
{
    [Theory]
    [InlineData("Ctrl+Shift+K", KeyModifiers.Ctrl | KeyModifiers.Shift, "K")]
    [InlineData("shift+tab", KeyModifiers.Shift, "tab")]
    [InlineData("Enter", KeyModifiers.None, "Enter")]
    [InlineData("Ctrl+1", KeyModifiers.Ctrl, "1")]
    public void ParsesChords(string text, KeyModifiers modifiers, string key) =>
        Assert.Equal(new KeyChord(modifiers, key), KeyChord.Parse(text));

    [Theory]
    [InlineData("Enter", typeof(SplitBlock))]
    [InlineData("Shift+Enter", typeof(InsertSoftBreak))]
    [InlineData("Ctrl+Enter", typeof(SplitBlockAlternate))]
    [InlineData("Tab", typeof(Indent))]
    [InlineData("Shift+Tab", typeof(Outdent))]
    [InlineData("Ctrl+Z", typeof(Undo))]
    [InlineData("Ctrl+Y", typeof(Redo))]
    [InlineData("Ctrl+3", typeof(SetHeadingLevel))]
    public void DefaultEditingKeys(string chord, Type command)
    {
        var binding = Assert.IsType<CommandBinding>(KeyMap.Default.Find(KeyChord.Parse(chord)));
        Assert.IsType(command, binding.Command);
    }

    [Theory]
    [InlineData("Ctrl+C", ClipboardAction.Copy)]
    [InlineData("Ctrl+Shift+C", ClipboardAction.CopyAsMarkdown)]
    [InlineData("Ctrl+X", ClipboardAction.Cut)]
    [InlineData("Ctrl+V", ClipboardAction.Paste)]
    [InlineData("Ctrl+Shift+V", ClipboardAction.PasteAsPlainText)]
    public void DefaultClipboardKeys(string chord, ClipboardAction action) =>
        Assert.Equal(new ClipboardBinding(action), KeyMap.Default.Find(KeyChord.Parse(chord)));

    [Fact]
    public void BindingsCanBeReplaced()
    {
        var map = KeyMap.Default.With("Ctrl+Enter", new CommandBinding(new SplitBlock()));
        Assert.IsType<SplitBlock>(((CommandBinding)map.Find(KeyChord.Parse("Ctrl+Enter"))!).Command);
        Assert.IsType<SplitBlockAlternate>(((CommandBinding)KeyMap.Default.Find(KeyChord.Parse("Ctrl+Enter"))!).Command);
    }
}
