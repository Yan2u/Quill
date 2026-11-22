using Quill.Highlighting;

namespace Quill.Editor.Tests.Highlighting;

/// <summary>
/// M6.3: code blocks of the editing mode colored in their language, re-analyzing only the lines an edit
/// changed until the grammar state settles.
/// </summary>
public sealed class CodeHighlighterTests
{
    private static CodeHighlighter Create(string info) =>
        CodeHighlighter.Create(HighlightTheme.Light, info) ?? throw new InvalidOperationException("No grammar for " + info);

    private static uint ColorAt(IReadOnlyList<HighlightSpan[]> lines, int line, int column) =>
        lines[line].Single(span => span.Start <= column && column < span.Start + span.Length).Foreground;

    [Theory]
    [InlineData("")]
    [InlineData("no-such-language")]
    public void NoLanguageOrGrammarHasNoHighlighter(string info) => Assert.Null(CodeHighlighter.Create(HighlightTheme.Light, info));

    [Theory]
    [InlineData("cs", "var x = 1;", 0)]
    [InlineData("python", "def f(): pass", 0)]
    [InlineData("rust", "fn main() {}", 0)]
    [InlineData("bash", "if true; then echo; fi", 0)]
    public void KeywordsAreColored(string info, string code, int keyword)
    {
        var highlighter = Create(info);
        var lines = highlighter.Highlight(code);
        Assert.NotEqual(highlighter.DefaultForeground, ColorAt(lines, 0, keyword));
    }

    [Fact]
    public void RunsCoverEachLineWithoutGaps()
    {
        string[] code = ["int a = 1; // one", "string s = \"two\";", "", "\treturn a;"];
        var lines = Create("c#").Highlight(string.Join("\r\n", code));
        Assert.Equal(code.Length, lines.Count);
        for (int line = 0; line < code.Length; line++)
        {
            Assert.Equal(code[line].Length, lines[line].Sum(static span => span.Length));
        }
    }

    [Fact]
    public void AnEditAnalyzesOnlyTheChangedLine()
    {
        var highlighter = Create("cs");
        string text = string.Join('\n', Enumerable.Range(0, 200).Select(static i => $"int v{i} = {i};"));
        highlighter.Highlight(text);
        Assert.Equal(200, highlighter.AnalyzedLines);

        highlighter.Highlight(text.Replace("int v100 = 100;", "int v100 = 1000;"));
        Assert.Equal(1, highlighter.AnalyzedLines);

        highlighter.Highlight(text.Replace("int v100 = 100;", "int v100 = 100;\nint w = 0;"));
        Assert.True(highlighter.AnalyzedLines <= 3, $"Analyzed {highlighter.AnalyzedLines} lines.");
    }

    [Fact]
    public void AnOpenedCommentRecolorsTheLinesAfterIt()
    {
        var highlighter = Create("cs");
        string text = "int a;\nint b;\nint c;";
        var before = highlighter.Highlight(text);
        var after = highlighter.Highlight("/*int a;\nint b;\nint c;");
        Assert.Equal(3, highlighter.AnalyzedLines);
        Assert.NotEqual(ColorAt(before, 2, 0), ColorAt(after, 2, 0));

        // Closed again: the lines after the comment get their colors back.
        var closed = highlighter.Highlight("/*int a;*/\nint b;\nint c;");
        Assert.Equal(ColorAt(before, 2, 0), ColorAt(closed, 2, 0));
    }

    [Fact]
    public void DarkThemeHasItsOwnColors()
    {
        var light = Create("cs").Highlight("var x = 1;");
        var dark = CodeHighlighter.Create(HighlightTheme.Dark, "cs")!.Highlight("var x = 1;");
        Assert.NotEqual(ColorAt(light, 0, 0), ColorAt(dark, 0, 0));
    }
}
