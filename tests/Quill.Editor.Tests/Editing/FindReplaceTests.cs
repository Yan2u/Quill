using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;
using Quill.Editor.Search;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// 12-find-replace.md (Q86, Typora checked by the user): in the editing mode every leaf's source is
/// searched (inline markers and link URLs found, block syntax not); in the source mode every character;
/// the options; jumping with wrap-around; replace and replace all (one undo item, paragraphs committed).
/// </summary>
public sealed class FindReplaceTests
{
    private static Document Import(string markdown) => MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;

    private static int Count(string markdown, string text, bool matchCase = false, bool wholeWord = false, bool regex = false)
    {
        var search = DocumentSearch.TryCreate(new SearchQuery(text, matchCase, wholeWord, regex), out string? error)!;
        Assert.Null(error);
        return search.All(Import(markdown)).Count();
    }

    [Theory]
    // Inline markers and link URLs are searchable, active or not.
    [InlineData("a **bold** b", "**", 2)]
    [InlineData("a *em* `code` ~sub~ ^sup^ b", "`", 2)]
    [InlineData("see [the text](https://example.com/page \"title\")", "example.com", 1)]
    [InlineData("see [the text](https://example.com/page \"title\")", "title", 1)]
    [InlineData("an ![alt](pic.png) image", "pic.png", 1)]
    [InlineData("a <u>under</u> b", "<u>", 1)]
    // Displayed text is; block syntax is not.
    [InlineData("# Heading", "Heading", 1)]
    [InlineData("# Heading", "#", 0)]
    [InlineData("- item\n- [ ] task", "-", 0)]
    [InlineData("- item\n- [ ] task", "[ ]", 0)]
    [InlineData("> quoted", ">", 0)]
    [InlineData("```js\nlet a;\n```", "```", 0)]
    [InlineData("```js\nlet a;\n```", "js", 0)]
    [InlineData("```js\nlet a;\n```", "let", 1)]
    [InlineData("| a | b |\n| --- | --- |\n| c | d |", "---", 0)]
    [InlineData("| a | b |\n| --- | --- |\n| c | d |", "c", 1)]
    // Footnote and link definitions: the slots, not the brackets and colon.
    [InlineData("x[^1]\n\n[^1]: footnote", "[^1]:", 0)]
    [InlineData("x[^1]\n\n[^1]: footnote", "footnote", 1)]
    [InlineData("x[^1]\n\n[^1]: footnote", "1", 2)]
    [InlineData("[a]: http://x.org \"t\"", "http://x.org", 1)]
    [InlineData("[a]: http://x.org \"t\"", "]:", 0)]
    // Display math is searchable while it shows as a formula.
    [InlineData("$$\nE = mc^2\n$$", "mc", 1)]
    [InlineData("$$\nE = mc^2\n$$", "$$", 0)]
    // The zero-width space before an indented paragraph is not text.
    [InlineData("​    indented", "​", 0)]
    public void TheEditingModeSearchesLeafSource(string markdown, string text, int count) => Assert.Equal(count, Count(markdown, text));

    [Fact]
    public void TheSourceModeSearchesEveryCharacter()
    {
        var source = SourceText.ToDocument("# Heading\n\n- item\n\n```js\nlet a;\n```", DocumentFormat.Default, new SequentialBlockIdSource());
        int Hits(string text) => DocumentSearch.TryCreate(new SearchQuery(text), out _)!.All(source).Count();
        Assert.Equal(1, Hits("#"));
        Assert.Equal(1, Hits("- "));
        Assert.Equal(2, Hits("```"));
        Assert.Equal(1, Hits("js"));
    }

    [Theory]
    [InlineData("Word word WORD", "word", false, false, false, 3)]
    [InlineData("Word word WORD", "word", true, false, false, 1)]
    [InlineData("word words sword word_x", "word", false, true, false, 1)]
    [InlineData("中文 中文字", "中文", false, true, false, 1)]
    [InlineData("a1 b22 c333", "[a-z]\\d+", false, false, true, 3)]
    [InlineData("a1 b22 c333", "\\d{2,}", false, false, true, 2)]
    [InlineData("aaa", "a*", false, false, true, 1)]
    public void Options(string markdown, string text, bool matchCase, bool wholeWord, bool regex, int count) =>
        Assert.Equal(count, Count(markdown, text, matchCase, wholeWord, regex));

    [Fact]
    public void AnInvalidExpressionIsReported()
    {
        Assert.Null(DocumentSearch.TryCreate(new SearchQuery("a(b", UseRegex: true), out string? error));
        Assert.NotNull(error);
        Assert.Null(DocumentSearch.TryCreate(new SearchQuery(string.Empty), out _));
    }

    [Fact]
    public void NextAndPreviousWrapAround()
    {
        var host = EditorTestHost.Load("x one<|> x\n\n- two x\n\nthree");
        var search = DocumentSearch.TryCreate(new SearchQuery("x"), out _)!;
        string Go(bool forward)
        {
            var match = search.Next(host.State.Document, host.State.Selection, forward)!.Value;
            host.Execute(new SetSelection(new TextSelection(new TextPosition(match.Block, match.Start), new TextPosition(match.Block, match.End)), Exact: true));
            return host.Dump();
        }

        Assert.Equal("x one <^>x<$>\n\n- two x\n\nthree", Go(forward: true));
        Assert.Equal("x one x\n\n- two <^>x<$>\n\nthree", Go(forward: true));
        Assert.Equal("<^>x<$> one x\n\n- two x\n\nthree", Go(forward: true));
        Assert.Equal("x one x\n\n- two <^>x<$>\n\nthree", Go(forward: false));
        Assert.Equal((3, 3), search.Count(host.State.Document, host.State.Selection));
    }

    [Fact]
    public void JumpingSelectsTheMatchExactlyAndActivatesTheLink()
    {
        var host = EditorTestHost.Load("<|>see [text](https://example.com/page) end");
        var search = DocumentSearch.TryCreate(new SearchQuery("text"), out _)!;
        var match = search.Next(host.State.Document, host.State.Selection, forward: true)!.Value;
        host.Execute(new SetSelection(new TextSelection(new TextPosition(match.Block, match.Start), new TextPosition(match.Block, match.End)), Exact: true));

        // Without Exact the markers would follow the selection (Q18); the match stays as found.
        Assert.Equal("see [<^>text<$>](https://example.com/page) end", host.Dump());
        var projection = host.Project();
        Assert.Contains(projection.Active.ActiveNodes, node => node.Kind == Quill.Editor.Inlines.InlineKind.Link);
    }

    [Fact]
    public void JumpingIntoDisplayMathFocusesIt()
    {
        var host = EditorTestHost.Load("<|>intro\n\n$$\nE = mc^2\n$$");
        var search = DocumentSearch.TryCreate(new SearchQuery("mc"), out _)!;
        var match = search.Next(host.State.Document, host.State.Selection, forward: true)!.Value;
        host.Execute(new SetSelection(new TextSelection(new TextPosition(match.Block, match.Start), new TextPosition(match.Block, match.End)), Exact: true));
        var math = host.State.Document.Children[1];
        var map = host.Project().Map[math.Id]!;
        Assert.Equal(2, map.ToDisplay(match.End, Affinity.Backward) - map.ToDisplay(match.Start, Affinity.Forward));
    }

    [Fact]
    public void ReplaceReplacesTheSelectedMatchOnly()
    {
        var host = EditorTestHost.Load("a <^>x<$> b x");
        host.Execute(new ReplaceMatch(new SearchQuery("x"), "yy"));
        Assert.Equal("a yy<|> b x", host.Dump());

        // Not a match: nothing happens.
        host.Execute(new ReplaceMatch(new SearchQuery("x"), "zz"));
        Assert.Equal("a yy<|> b x", host.Dump());
    }

    [Fact]
    public void ReplaceAllIsOneUndoItem()
    {
        var host = EditorTestHost.Load("x one<|> x\n\n| x | y |\n| - | - |\n\n- two x");
        host.Execute(new ReplaceAllMatches(new SearchQuery("x"), "Z"));
        Assert.Equal("Z one<|> Z\n\n| Z | y |\n| - | - |\n\n- two Z", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("x one<|> x\n\n| x | y |\n| - | - |\n\n- two x", host.Dump());
    }

    [Fact]
    public void RegexReplacementsExpandGroups()
    {
        var host = EditorTestHost.Load("<|>2026-10-31 and 1999-01-02");
        host.Execute(new ReplaceAllMatches(new SearchQuery(@"(\d{4})-(\d{2})-(\d{2})", UseRegex: true), "$3/$2/$1"));
        Assert.Equal("<|>31/10/2026 and 02/01/1999", host.Dump());
    }

    [Fact]
    public void ReplacingInACellEscapesPipes()
    {
        var host = EditorTestHost.Load("| a<|> | b |\n| - | - |");
        host.Execute(new ReplaceAllMatches(new SearchQuery("a"), "x|y"));
        Assert.Equal("| x\\|y<|> | b |\n| - | - |", host.Dump());
    }

    [Theory]
    [InlineData("<|>title", "title", "# title", "# title")]
    [InlineData("<|>one\nmark two", "mark", "- mark", "one\n\n- mark two")]
    [InlineData("<|>x code", "x", "    x", "​    x code")]
    public void ReplacedParagraphsAreCommitted(string before, string text, string replacement, string markdown)
    {
        var host = EditorTestHost.Load(before);
        host.Execute(new ReplaceAllMatches(new SearchQuery(text), replacement));
        string written = MarkdownSerializer.Serialize(host.State.Document).TrimEnd('\n');
        Assert.Equal(markdown, written);
        var reopened = Import(written);
        Assert.Equal(host.State.Document.Children.Select(block => block.Kind), reopened.Children.Select(block => block.Kind));
    }

    [Fact]
    public void ReplacingInlineMarkersChangesTheFormat()
    {
        var host = EditorTestHost.Load("<|>a **b** c");
        host.Execute(new ReplaceAllMatches(new SearchQuery("**"), "*"));
        Assert.Equal("<|>a *b* c", host.Dump());
    }

    [Fact]
    public void TheSourceModeReplacesInLinesWithoutParsing()
    {
        var host = new SourceEditingTests.SourceHost("<|>alpha\nbeta");
        host.Execute(new ReplaceAllMatches(new SearchQuery("a"), "# "));
        Assert.Equal("<|># lph# \nbet# ", host.Dump());
    }

    [Fact]
    public void UnchangedLeavesAreNotSearchedAgain()
    {
        var host = EditorTestHost.Load("<|>alpha\n\nbeta\n\ngamma");
        var search = DocumentSearch.TryCreate(new SearchQuery("a"), out _)!;
        var before = search.MatchesIn((LeafBlock)host.State.Document.Children[1]);
        host.Type("x");
        Assert.Same(before, search.MatchesIn((LeafBlock)host.State.Document.Children[1]));
        Assert.Equal(5, search.All(host.State.Document).Count());
    }

    [Fact]
    public void APositionOfAnotherDocumentSearchesFromTheStart()
    {
        var search = DocumentSearch.TryCreate(new SearchQuery("x"), out _)!;
        var document = Import("a x\n\nb x");
        var foreign = TextSelection.Caret(new TextPosition(new BlockId(999_999), 3));
        var match = search.Next(document, foreign, forward: true)!.Value;
        Assert.Equal((document.Children[0].Id, 2), (match.Block, match.Start));
    }

    [Theory]
    [InlineData("a word b", "a writer b", false)]
    [InlineData("a word b", "# word b", true)]
    [InlineData("x\n-y", "x\n- y", true)]
    [InlineData("a\n--- | ---", "a | b\n--- | ---", true)]
    [InlineData("one\ntwo", "one\n", true)]
    [InlineData("one\ntwo", "one\n2. two", true)]
    [InlineData("one\ntwo", "ONE\ntwo", false)]
    public void OnlyLinesThatMayStartABlockNeedACommit(string before, string after, bool expected) =>
        Assert.Equal(expected, SearchEdits.MayStartBlock(before, after));

    [Fact]
    public void ReplacingCanTurnAParagraphIntoATable()
    {
        var host = EditorTestHost.Load("<|>a\n--- | ---");
        host.Execute(new ReplaceAllMatches(new SearchQuery("a"), "a | b"));
        Assert.IsType<Table>(host.State.Document.Children[0]);
    }
}
