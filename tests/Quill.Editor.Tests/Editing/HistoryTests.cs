using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Undo and redo (04): merging of consecutive typing and deletion, the idle threshold, breaks on
/// caret jumps, selection changes outside the history; IME compositions as one item (03, 07).
/// </summary>
public sealed class HistoryTests
{
    [Fact]
    public void ConsecutiveTypingIsOneItem()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("abc");
        Assert.Single(host.Session.History.UndoItems);
        host.Press("Ctrl+Z");
        Assert.Equal("<|>", host.Dump());
        host.Press("Ctrl+Y");
        Assert.Equal("abc<|>", host.Dump());
    }

    [Fact]
    public void IdleTimeBreaksMerging()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("ab").Advance(TimeSpan.FromSeconds(2)).Type("c");
        host.Press("Ctrl+Z");
        Assert.Equal("ab<|>", host.Dump());
    }

    [Fact]
    public void CaretJumpBreaksMerging()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("ab").Press("Left").Type("c");
        host.Press("Ctrl+Z");
        Assert.Equal("a<|>b", host.Dump());
    }

    [Fact]
    public void TypingAndDeletingAreSeparateItems()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("abc").Press("Backspace Backspace");
        host.Press("Ctrl+Z");
        Assert.Equal("abc<|>", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("<|>", host.Dump());
    }

    [Fact]
    public void SelectionChangesAreNotUndoItems()
    {
        var host = EditorTestHost.Load("ab<|>c");
        host.Press("Left Shift+Right Ctrl+A");
        Assert.False(host.Session.History.CanUndo);
    }

    [Fact]
    public void UndoRestoresSelectionBefore()
    {
        var host = EditorTestHost.Load("a<^>bc<$>d");
        host.Type("x");
        host.Press("Ctrl+Z");
        Assert.Equal("a<^>bc<$>d", host.Dump());
    }

    [Fact]
    public void NewEditClearsRedo()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("a").Press("Ctrl+Z").Type("b");
        Assert.False(host.Session.History.CanRedo);
        host.Press("Ctrl+Y");
        Assert.Equal("b<|>", host.Dump());
    }

    [Fact]
    public void UndoAndRedoAcrossStructuralEdits()
    {
        const string start = "- a<|>\n- b";
        var host = EditorTestHost.Load(start);
        host.Press("Enter").Type("x").Press("Tab").Press("Enter Enter");
        string end = host.Dump();
        int items = host.Session.History.UndoItems.Count;
        for (int i = 0; i < items; i++)
        {
            host.Press("Ctrl+Z");
        }

        Assert.Equal(start, host.Dump());
        for (int i = 0; i < items; i++)
        {
            host.Press("Ctrl+Y");
        }

        Assert.Equal(end, host.Dump());
    }

    // IME: a composition is one undo item; its updates are not separate items (03).
    [Fact]
    public void CompositionIsOneUndoItem()
    {
        var host = EditorTestHost.Load("a<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart())
            .Execute(new ReplaceRange(block, 1, 1, "n", 2))
            .Execute(new ReplaceRange(block, 1, 2, "ni", 3))
            .Execute(new ReplaceRange(block, 1, 3, "你", 2))
            .Execute(new CompositionEnd());
        Assert.Equal("a你<|>", host.Dump());
        Assert.Single(host.Session.History.UndoItems);
        host.Press("Ctrl+Z");
        Assert.Equal("a<|>", host.Dump());
    }

    [Fact]
    public void CancelledCompositionLeavesNoTrace()
    {
        var host = EditorTestHost.Load("a<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart())
            .Execute(new ReplaceRange(block, 1, 1, "ni", 3))
            .Execute(new CompositionEnd(Cancel: true));
        Assert.Equal("a<|>", host.Dump());
        Assert.False(host.Session.History.CanUndo);
    }

    // A composition started in a virtual paragraph makes it real on the first update, and a
    // cancellation takes that back too (03, 07 rule 10).
    [Theory]
    [InlineData(false, "```\na\n```\n\n你<|>")]
    [InlineData(true, "```\na\n```\n\n<gap>")]
    public void CompositionInVirtualParagraph(bool cancel, string after)
    {
        var host = EditorTestHost.Load("```\na\n```\n\n<gap>");
        host.Execute(new CompositionStart())
            .Execute(new ReplaceRange(new BlockId(999_999), 0, 0, "你", 1))
            .Execute(new CompositionEnd(cancel));
        Assert.Equal(after, host.Dump());
    }

    [Fact]
    public void NoInputRulesDuringComposition()
    {
        var host = EditorTestHost.Load("<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new CompositionStart()).Execute(new ReplaceRange(block, 0, 0, "# ", 2));
        Assert.IsType<Paragraph>(Assert.Single(host.State.Document.Children));
        host.Execute(new CompositionEnd());
        Assert.IsType<Heading>(Assert.Single(host.State.Document.Children));
    }

    [Fact]
    public void ActivationHeadStaysDuringComposition()
    {
        var host = EditorTestHost.Load("ab<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        var before = host.State.ActivationHead;
        host.Execute(new CompositionStart()).Execute(new ReplaceRange(block, 2, 2, "xy", 4));
        Assert.Equal(before, host.State.ActivationHead);
        Assert.Equal(TextSelection.Caret(block, 4), host.State.Selection);
    }

    [Fact]
    public void ActivationHeadFrozenWhilePointerIsDown()
    {
        var host = EditorTestHost.Load("ab<|>\n\ncd");
        var before = host.State.Selection.Head;
        host.Execute(new SetInteraction(Interaction.Default with { IsPointerDown = true }));
        host.Press("Down");
        Assert.Equal(before, host.State.ActivationHead);
        host.Execute(new SetInteraction(Interaction.Default));
        Assert.Equal(host.State.Selection.Head, host.State.ActivationHead);
    }
}
