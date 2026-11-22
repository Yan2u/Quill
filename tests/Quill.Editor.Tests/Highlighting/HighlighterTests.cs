using System.Diagnostics;
using Quill.Highlighting;

namespace Quill.Editor.Tests.Highlighting;

/// <summary>
/// 11-source-mode.md: Markdown source highlighted with the Light+ and Dark+ themes, fenced code in its
/// language (embedded by the Markdown grammar or found through the language map), incrementally.
/// </summary>
public sealed class HighlighterTests
{
    private const uint Blue = 0xFF0000FF;

    private static MarkdownHighlighter Highlight(params string[] lines)
    {
        var highlighter = new MarkdownHighlighter(HighlightTheme.Light, lines);
        highlighter.Run(TimeSpan.MaxValue);
        Assert.True(highlighter.IsComplete);
        return highlighter;
    }

    /// <summary>The color of the run covering a character of a line.</summary>
    private static uint ColorAt(MarkdownHighlighter highlighter, int line, int column) =>
        highlighter.SpansOf(line)!.Single(span => span.Start <= column && column < span.Start + span.Length).Foreground;

    [Theory]
    [InlineData("cs", "csharp")]
    [InlineData("C#", "csharp")]
    [InlineData("bash", "shellscript")]
    [InlineData("yml", "yaml")]
    [InlineData("  python {.numberLines}", "python")]
    [InlineData("language-rust", "rust")]
    [InlineData("", null)]
    public void LanguageMapNamesTheGrammar(string info, string? language) => Assert.Equal(language, LanguageMap.LanguageOf(info));

    [Fact]
    public void RunsCoverEveryLineWithoutGaps()
    {
        var highlighter = Highlight("# Title **bold**", "text *em* `code` [l](u)", "", "- item");
        for (int line = 0; line < highlighter.LineCount; line++)
        {
            int next = 0;
            foreach (var span in highlighter.SpansOf(line)!)
            {
                Assert.Equal(next, span.Start);
                next += span.Length;
            }
        }
    }

    [Fact]
    public void MarkdownSyntaxIsColored()
    {
        var highlighter = Highlight("# Title", "plain **bold** text");
        Assert.NotEqual(highlighter.DefaultForeground, ColorAt(highlighter, 0, 2));
        Assert.Contains(highlighter.SpansOf(1)!, span => span.Style.HasFlag(HighlightFontStyle.Bold));
    }

    [Theory]
    [InlineData("csharp", "var x = 1;")]   // embedded by the Markdown grammar
    [InlineData("cs", "var x = 1;")]       // an alias the Markdown grammar does not know: through the map
    [InlineData("c#", "var x = 1;")]
    [InlineData("hlsl", "float x = 1;")]   // not embedded by the Markdown grammar at all
    public void FencedCodeUsesItsLanguage(string info, string code)
    {
        var highlighter = Highlight("text", "```" + info, code, "```", "after");
        Assert.Equal(Blue, ColorAt(highlighter, 2, 0));
    }

    [Fact]
    public void AnUnknownLanguageShowsPlainCode()
    {
        var highlighter = Highlight("```nosuchlanguage", "var x = 1;", "```");
        Assert.NotEqual(Blue, ColorAt(highlighter, 1, 0));
    }

    [Fact]
    public void TheFenceEndsWhereItCloses()
    {
        var highlighter = Highlight("```cs", "var x;", "```", "var y;");
        Assert.Equal(Blue, ColorAt(highlighter, 1, 0));
        Assert.NotEqual(Blue, ColorAt(highlighter, 3, 0));
    }

    [Fact]
    public void AnEditAnalyzesOnlyUntilTheStateSettles()
    {
        var lines = Enumerable.Range(0, 2000).Select(i => i % 10 == 0 ? $"## Heading {i}" : $"Some *text* {i}").ToArray();
        var highlighter = Highlight(lines);
        highlighter.Replace(1000, 1, ["Some *edited* text"]);
        var (first, count) = highlighter.Run(TimeSpan.MaxValue);
        Assert.Equal(1000, first);
        Assert.InRange(count, 1, 2);
        Assert.True(highlighter.IsComplete);
    }

    [Fact]
    public void OpeningAFenceRecolorsTheLinesAfterIt()
    {
        var highlighter = Highlight("text", "var x = 1;", "more");
        highlighter.Replace(1, 0, ["```cs"]);
        var (_, count) = highlighter.Run(TimeSpan.MaxValue);
        Assert.Equal(3, count);
        Assert.Equal(Blue, ColorAt(highlighter, 2, 0));
    }

    [Fact]
    public void ABudgetLeavesTheRestForLater()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => $"Line *{i}* with `code`").ToArray();
        var highlighter = new MarkdownHighlighter(HighlightTheme.Light, lines);
        highlighter.Run(TimeSpan.FromMilliseconds(5));
        Assert.False(highlighter.IsComplete);
        Assert.Null(highlighter.SpansOf(4999));
        while (!highlighter.IsComplete)
        {
            highlighter.Run(TimeSpan.FromMilliseconds(5));
        }

        Assert.NotNull(highlighter.SpansOf(4999));
    }

    [Fact]
    public void DarkPlusHasItsOwnColors()
    {
        var light = Highlight("```cs", "var x = 1;", "```");
        var dark = new MarkdownHighlighter(HighlightTheme.Dark, ["```cs", "var x = 1;", "```"]);
        dark.Run(TimeSpan.MaxValue);
        Assert.NotEqual(ColorAt(light, 1, 0), ColorAt(dark, 1, 0));
        Assert.Equal(0xFF569CD6, ColorAt(dark, 1, 0));
    }

    [Fact]
    public void TypingInALineCostsWellUnderAMillisecond()
    {
        var lines = Enumerable.Range(0, 5000).Select(i => i % 7 == 0 ? $"## Heading {i}" : $"Some *text* with **bold**, `code` and [a link](https://example.com/{i}).").ToArray();
        var highlighter = Highlight(lines);
        string line = lines[2500];
        var times = new List<double>();
        for (int i = 0; i < 200; i++)
        {
            line = line.Insert(10, "x");
            var watch = Stopwatch.StartNew();
            highlighter.Replace(2500, 1, [line]);
            highlighter.Run(TimeSpan.MaxValue);
            times.Add(watch.Elapsed.TotalMilliseconds);
        }

        times.Sort();
        Assert.True(times[times.Count / 2] < 1, $"median {times[times.Count / 2]:F3} ms");
    }

    [Fact]
    public void LinesInViewAreColoredAheadOfTheAnalysis()
    {
        var lines = Enumerable.Range(0, 3000).Select(i => i == 2000 ? "```cs" : i == 2001 ? "var x = 1;" : $"Line *{i}*").ToArray();
        var highlighter = new MarkdownHighlighter(HighlightTheme.Light, lines);
        highlighter.Run(TimeSpan.FromMilliseconds(1));
        Assert.Null(highlighter.SpansOf(2001));
        Assert.Equal(3, highlighter.Preview(2000, 3));
        Assert.Equal(Blue, ColorAt(highlighter, 2001, 0));
        Assert.False(highlighter.IsAnalyzed(2001));
        while (!highlighter.IsComplete)
        {
            highlighter.Run(TimeSpan.FromMilliseconds(50));
        }

        Assert.True(highlighter.IsAnalyzed(2001));
        Assert.Equal(Blue, ColorAt(highlighter, 2001, 0));
    }
}
