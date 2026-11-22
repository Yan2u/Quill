using Quill.Editor.Commands;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// The block caret on a TOC or a thematic break (Q59, Q60, Typora): Up or Down stops on it and selects
/// it whole, the next move leaves it; Backspace and Delete delete it, other keys do nothing; the block
/// is read-only and is drawn unlike a selection.
/// </summary>
public sealed class BlockCaretTests
{
    [Theory]
    [InlineData("a<|>\n\n[toc]\n\nb", "Down", "a\n\n<block>\n\n[toc]\n\nb")]
    [InlineData("a\n\n[toc]\n\n<|>b", "Up", "a\n\n<block>\n\n[toc]\n\nb")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Down", "a\n\n[toc]\n\n<|>b")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Up", "a<|>\n\n[toc]\n\nb")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Right", "a\n\n[toc]\n\n<|>b")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Left", "a<|>\n\n[toc]\n\nb")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Home", "a\n\n<block>\n\n[toc]\n\nb")]
    [InlineData("a\n\n<block>\n\n[toc]\n\nb", "Ctrl+End", "a\n\n[toc]\n\nb<|>")]
    [InlineData("a<|>\n\n[toc]\n\nb", "Right", "a\n\n[toc]\n\n<|>b")]
    public void UpAndDownStopOnTheToc(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    // The TOC first in the document: the virtual paragraph before it is reached from the block caret.
    [InlineData("[toc]\n\n<|>a", "Up", "<block>\n\n[toc]\n\na")]
    [InlineData("<block>\n\n[toc]\n\na", "Up", "<gap>\n\n[toc]\n\na")]
    [InlineData("<gap>\n\n[toc]\n\na", "Down", "<block>\n\n[toc]\n\na")]
    // Two TOCs: the virtual paragraph between them, then the second.
    [InlineData("a<|>\n\n[toc]\n\n[toc]\n\nb", "Down Down", "a\n\n[toc]\n\n<gap>\n\n[toc]\n\nb")]
    [InlineData("a<|>\n\n[toc]\n\n[toc]\n\nb", "Down Down Down", "a\n\n[toc]\n\n<block>\n\n[toc]\n\nb")]
    // Inside a quote, and leaving the quote.
    [InlineData("> a<|>\n>\n> [toc]\n\nb", "Down", "> a\n>\n> <block>\n>\n> [toc]\n\nb")]
    [InlineData("> a\n>\n> <block>\n>\n> [toc]\n\nb", "Down", "> a\n>\n> [toc]\n\n<|>b")]
    public void BlockCaretAtEdgesAndInContainers(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    // Leaving by Up or Down keeps the goal X of the move that reached the block caret (M5.2).
    [InlineData("abc<|>def\n\n[toc]\n\nxyz123", "Down Down", "abcdef\n\n[toc]\n\nxyz<|>123")]
    [InlineData("abcdef\n\n[toc]\n\nxyz<|>123", "Up Up", "abc<|>def\n\n[toc]\n\nxyz123")]
    [InlineData("abc<|>def\n\n---\n\nxy", "Down Down", "abcdef\n\n---\n\nxy<|>")]
    // The virtual paragraph between the rule and the TOC is one more stop on the way.
    [InlineData("abc<|>def\n\n---\n\n[toc]\n\nxyz123", "Down Down Down Down", "abcdef\n\n---\n\n[toc]\n\nxyz<|>123")]
    public void LeavingTheBlockCaretKeepsTheGoalX(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Theory]
    [InlineData("Backspace", "a<|>\n\nb")]
    [InlineData("Delete", "a<|>\n\nb")]
    public void BackspaceAndDeleteDeleteTheToc(string key, string after)
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[toc]\n\nb");
        host.Press(key);
        Assert.Equal(after, host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("a\n\n<block>\n\n[toc]\n\nb", host.Dump());
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("Shift+Enter")]
    [InlineData("Ctrl+Enter")]
    [InlineData("Tab")]
    [InlineData("Shift+Tab")]
    [InlineData("Ctrl+V")]
    [InlineData("Ctrl+2")]
    public void OtherKeysDoNothing(string key)
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[toc]\n\nb");
        host.Clipboard = new ClipboardContent(null, "pasted");
        host.Press(key);
        Assert.Equal("a\n\n<block>\n\n[toc]\n\nb", host.Dump());
        Assert.False(host.Session.History.CanUndo);
    }

    [Fact]
    public void TypingAndCompositionDoNothing()
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[toc]\n\nb");
        host.Type("xyz");
        host.Execute(new CompositionStart());
        host.Execute(new ReplaceRange(host.State.Document.Children[0].Id, 0, 0, "中", 1));
        host.Execute(new CompositionEnd());
        Assert.Equal("a\n\n<block>\n\n[toc]\n\nb", host.Dump());
    }

    [Fact]
    public void CopyTakesTheTocAndCutDeletesIt()
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[toc]\n\nb");
        host.Press("Ctrl+C");
        Assert.Equal("[toc]", host.Clipboard!.Markdown);
        host.Press("Ctrl+X");
        Assert.Equal("a<|>\n\nb", host.Dump());
    }

    [Fact]
    public void ShiftArrowsSelectFromTheFarSide()
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[toc]\n\nbc");
        host.Press("Shift+Down");
        Assert.False(host.State.Selection.IsBlockCaret);
        // Without a layout, Down from the boundary after the TOC reaches the start of the next block.
        Assert.Equal("a\n\n<gap:^>\n\n[toc]\n\n<$>bc", host.Dump());
    }

    [Fact]
    public void ExtendingDownPassesOverTheToc()
    {
        var host = EditorTestHost.Load("a<|>\n\n[toc]\n\nb");
        host.Press("Shift+Down");
        Assert.False(host.State.Selection.IsBlockCaret);
    }

    [Fact]
    public void ADraggedSelectionOfTheTocIsNotABlockCaret()
    {
        var host = EditorTestHost.Load("a\n\n<gap:^>\n\n[toc]\n\n<gap:$>\n\nb");
        Assert.False(host.State.Selection.IsBlockCaret);
        var toc = ProjectionDump.Blocks(host.Project())[1];
        Assert.Equal(true, ProjectionDump.Attribute(toc, ProjectionNames.IsSelected));
        Assert.Null(ProjectionDump.Attribute(toc, ProjectionNames.HasBlockCaret));
        Assert.Null(ProjectionDump.Attribute(toc, ProjectionNames.IsActive));

        // Typing over a selection replaces it, unlike on a block caret.
        host.Type("x");
        Assert.Equal("a\n\nx<|>\n\nb", host.Dump());
    }

    [Fact]
    public void BlockCaretShowsTheReadOnlySource()
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n[TOC]\n\nb");
        var result = host.Project();
        var toc = ProjectionDump.Blocks(result)[1];
        Assert.Equal(ProjectionNames.Toc, toc.Type);
        Assert.Equal(true, ProjectionDump.Attribute(toc, ProjectionNames.HasBlockCaret));
        Assert.Equal(true, ProjectionDump.Attribute(toc, ProjectionNames.IsActive));
        Assert.Null(ProjectionDump.Attribute(toc, ProjectionNames.IsSelected));
        Assert.Equal("[TOC]", ProjectionDump.Attribute(toc, ProjectionNames.Source));
        Assert.True(result.Selection.IsBlockCaret);
        Assert.Equal(DocumentProjector.KeyOf(result.Active.FocusedBlock!.Value), toc.Key);

        host.Press("Down");
        var left = ProjectionDump.Blocks(host.Project())[1];
        Assert.Null(ProjectionDump.Attribute(left, ProjectionNames.HasBlockCaret));
        Assert.Null(ProjectionDump.Attribute(left, ProjectionNames.IsActive));
    }

    [Fact]
    public void AnInvalidBlockCaretBecomesASelection()
    {
        var host = EditorTestHost.Load("<#p>a\n\nb<|>");
        host.Execute(new SetSelection(TextSelection.BlockCaret(host.State.Document.Id, 0)));
        Assert.False(host.State.Selection.IsBlockCaret);
    }

    [Theory]
    // Thematic breaks take the block caret like the TOC (Q60).
    [InlineData("a<|>\n\n---\n\nb", "Down", "a\n\n<block>\n\n---\n\nb")]
    [InlineData("a\n\n***\n\n<|>b", "Up", "a\n\n<block>\n\n***\n\nb")]
    [InlineData("a\n\n<block>\n\n---\n\nb", "Down", "a\n\n---\n\n<|>b")]
    [InlineData("a\n\n<block>\n\n---\n\nb", "Backspace", "a<|>\n\nb")]
    [InlineData("a\n\n<block>\n\n---\n\nb", "Delete", "a<|>\n\nb")]
    [InlineData("a\n\n<block>\n\n---\n\nb", "Enter", "a\n\n<block>\n\n---\n\nb")]
    [InlineData("a<|>\n\n---\n\n[toc]\n\nb", "Down Down Down", "a\n\n---\n\n<block>\n\n[toc]\n\nb")]
    [InlineData("- a<|>\n\n  ---\n- b", "Down", "- a\n\n  <block>\n\n  ---\n- b")]
    public void ThematicBreaksTakeTheBlockCaret(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void RuleUnderABlockCaretIsMarkedButShowsNoSource()
    {
        var host = EditorTestHost.Load("a\n\n<block>\n\n---\n\nb");
        var rule = ProjectionDump.Blocks(host.Project())[1];
        Assert.Equal(ProjectionNames.ThematicBreak, rule.Type);
        Assert.Equal(true, ProjectionDump.Attribute(rule, ProjectionNames.HasBlockCaret));
        Assert.Null(ProjectionDump.Attribute(rule, ProjectionNames.IsActive));
        Assert.Null(ProjectionDump.Attribute(rule, ProjectionNames.IsSelected));
    }
}
