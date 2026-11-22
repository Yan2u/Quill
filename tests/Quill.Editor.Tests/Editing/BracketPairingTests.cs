using Quill.Editor.Commands;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Bracket and quote pairing (04, Q107, Q115): ( [ { " ' get their closing half, except before a letter or
/// digit and (quotes) after one; typing the added closing half steps over it; Backspace in an empty pair
/// deletes both; a selection in one block is wrapped; one undo item; nothing pairs while composing.
/// </summary>
public sealed class BracketPairingTests
{
    private static readonly EditorOptions Off = EditorOptions.Default with { MatchBrackets = false };

    [Theory]
    [InlineData("a <|>", "(", "a (<|>)")]
    [InlineData("a <|>", "[", "a [<|>]")]
    [InlineData("a <|>", "{", "a {<|>}")]
    [InlineData("a <|>", "\"", "a \"<|>\"")]
    [InlineData("a <|>", "'", "a '<|>'")]
    [InlineData("<|>", "(", "(<|>)")]
    [InlineData("a <|> b", "(", "a (<|>) b")]
    [InlineData("<|>)", "(", "(<|>))")]
    [InlineData("<|>, b", "\"", "\"<|>\", b")]
    public void AnOpeningHalfGetsItsClosingHalf(string state, string typed, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state).Type(typed).Dump());
    }

    [Theory]
    [InlineData("<|>word", "(", "(<|>word")]
    [InlineData("<|>2", "[", "[<|>2")]
    [InlineData("don<|>", "'", "don'<|>")]
    [InlineData("say<|>", "\"", "say\"<|>")]
    [InlineData("a<|>", "(", "a(<|>)")]
    [InlineData("a <|>", ")", "a )<|>")]
    [InlineData("a <|>", "<", "a <<|>")]
    [InlineData("a <|>", "（", "a （<|>")]
    public void SomePlacesDoNotPair(string state, string typed, string expected)
    {
        // Q115: not before a letter or digit; a quote not after one (don't); not < nor CJK brackets (Q107).
        Assert.Equal(expected, EditorTestHost.Load(state).Type(typed).Dump());
    }

    [Theory]
    [InlineData("a <|>", "(x)", "a (x)<|>")]
    [InlineData("a <|>", "[link](u)", "a [link](u)<|>")]
    [InlineData("a <|>", "([])", "a ([])<|>")]
    [InlineData("a <|>", "\"q\"", "a \"q\"<|>")]
    [InlineData("a <|>", "f(x, (y))", "a f(x, (y))<|>")]
    public void TypingTheClosingHalfStepsOverTheAddedOne(string state, string typed, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state).Type(typed).Dump());
    }

    [Fact]
    public void AClosingHalfThatWasNotAddedIsTyped()
    {
        Assert.Equal("(a)<|>)", EditorTestHost.Load("(a<|>)").Type(")").Dump());
    }

    [Fact]
    public void TheAddedClosingHalfFollowsTheCaretWithinItsBlock()
    {
        var host = EditorTestHost.Load("a <|>").Type("(xy");
        host.Press("Left Left Right Right");
        Assert.Equal("a (xy)<|>", host.Type(")").Dump());
    }

    [Fact]
    public void LeavingTheBlockForgetsTheAddedClosingHalf()
    {
        var host = EditorTestHost.Load("a <|>\n\nb").Type("(");
        host.Press("Ctrl+End Ctrl+Home End Left");
        Assert.Equal("a ()<|>)\n\nb", host.Type(")").Dump());
    }

    [Theory]
    [InlineData("a (<|>)", "a <|>")]
    [InlineData("a \"<|>\"", "a <|>")]
    [InlineData("[<|>]x", "<|>x")]
    [InlineData("a (<|>x)", "a <|>x)")]
    [InlineData("a (<|>]", "a <|>]")]
    public void BackspaceInAnEmptyPairDeletesBoth(string state, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state).Press("Backspace").Dump());
    }

    [Theory]
    [InlineData("a <^>bc<$> d", "(", "a (<^>bc<$>) d")]
    [InlineData("a <$>bc<^> d", "[", "a [<$>bc<^>] d")]
    [InlineData("a <^>bc<$> d", "\"", "a \"<^>bc<$>\" d")]
    public void ASelectionInOneBlockIsWrapped(string state, string typed, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state).Type(typed).Dump());
    }

    [Fact]
    public void ASelectionAcrossBlocksIsReplaced()
    {
        // A selection across blocks cannot be wrapped: it is typed over, without pairing.
        Assert.Equal("a(<|>d", EditorTestHost.Load("a<^>b\n\nc<$>d").Type("(").Dump());
        Assert.Equal("a(<|> d", EditorTestHost.Load("a<^>b\n\nc<$> d").Type("(").Dump());
    }

    [Fact]
    public void ThePairIsOneUndoItem()
    {
        var host = EditorTestHost.Load("a <|>").Type("(");
        Assert.Equal("a <|>", host.Press("Ctrl+Z").Dump());
        Assert.Equal("a (<|>)", host.Press("Ctrl+Y").Dump());
    }

    [Fact]
    public void AWrappedSelectionIsOneUndoItem()
    {
        var host = EditorTestHost.Load("a <^>bc<$> d").Type("(");
        Assert.Equal("a <^>bc<$> d", host.Press("Ctrl+Z").Dump());
    }

    [Theory]
    [InlineData("```\n<|>\n```", "{", "```\n{<|>}\n```")]
    [InlineData("$$\n<|>\n$$", "(", "$$\n(<|>)\n$$")]
    [InlineData("| a<|> |\n| - |", "(", "| a(<|>) |\n| - |")]
    [InlineData("# t <|>", "[", "# t [<|>]")]
    public void PairingWorksWhereverTextIsTyped(string state, string typed, string expected)
    {
        Assert.Equal(expected, EditorTestHost.Load(state).Type(typed).Dump());
    }

    [Fact]
    public void ATaskItemIsStillTypedAsBefore()
    {
        var expected = EditorTestHost.Load("- [ ] task<|>").Dump();
        Assert.Equal(expected, EditorTestHost.Load("<|>").Type("- [ ] task").Dump());
    }

    [Theory]
    [InlineData("a <|>", "(", "a (<|>")]
    [InlineData("a (<|>)", "", "a <|>)")]
    [InlineData("a <^>bc<$> d", "(", "a (<|> d")]
    public void WithTheSettingOffNothingPairs(string state, string typed, string expected)
    {
        var host = EditorTestHost.Load(state, Off);
        if (typed.Length == 0)
        {
            host.Press("Backspace");
        }

        Assert.Equal(expected, host.Type(typed).Dump());
    }

    [Fact]
    public void ACompositionThatCommitsAnOpeningHalfPairsItInTheSameUndoItem()
    {
        var host = EditorTestHost.Load("a <|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart()).Execute(new ReplaceRange(block, 2, 2, "(", 3)).Execute(new CompositionEnd());
        Assert.Equal("a (<|>)", host.Dump());
        Assert.Equal("a <|>", host.Press("Ctrl+Z").Dump());
    }

    [Fact]
    public void ACompositionOfTextDoesNotPair()
    {
        var host = EditorTestHost.Load("a <|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart()).Execute(new ReplaceRange(block, 2, 2, "(中", 4)).Execute(new CompositionEnd());
        Assert.Equal("a (中<|>", host.Dump());
    }

    [Fact]
    public void NothingPairsWhileComposing()
    {
        var host = EditorTestHost.Load("a <|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart()).Execute(new ReplaceRange(block, 2, 2, "(", 3));
        Assert.Equal("a (<|>", host.Dump());
        host.Execute(new CompositionEnd(Cancel: true));
        Assert.Equal("a <|>", host.Dump());
    }

    [Fact]
    public void TheOptionsCanChangeDuringASession()
    {
        var host = EditorTestHost.Load("a <|>", Off).Type("(");
        host.Session.SetOptions(EditorOptions.Default);
        Assert.Equal("a ([<|>]", host.Type("[").Dump());
    }
}
