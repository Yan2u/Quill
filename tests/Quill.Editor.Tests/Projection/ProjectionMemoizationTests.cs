using Quill.Editor.Commands;
using Quill.Editor.Diagnostics;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// Memoization and the work per keystroke (05-projection-and-view.md, 09-performance.md): unchanged
/// inputs give the same node instances, an edit re-projects only the edited block and its ancestors,
/// and no edit or undo does a full projection.
/// </summary>
public sealed class ProjectionMemoizationTests
{
    private const string LongDocument = "<#first>first para\n\n<#second>second **bold**<|> text\n\n- item\n- item **two**\n\n> quote\n\n<#last>last";

    [Fact]
    public void UnchangedStateGivesTheSameRoot()
    {
        var host = EditorTestHost.Load(LongDocument);
        var first = host.Project();
        var second = host.Project();
        Assert.Same(first.Root, second.Root);
        Assert.Equal(0, second.Statistics.BlocksProjected);
    }

    [Fact]
    public void TypingReprojectsOnlyTheEditedBlock()
    {
        var host = EditorTestHost.Load(LongDocument);
        var before = host.Project();
        host.Type("x");
        var after = host.Project();
        Assert.Equal(1, after.Statistics.BlocksProjected);
        Assert.Equal(1, after.Statistics.LeavesRendered);
        var oldBlocks = ProjectionDump.Blocks(before);
        var newBlocks = ProjectionDump.Blocks(after);
        Assert.Same(oldBlocks[0], newBlocks[0]);
        Assert.NotSame(oldBlocks[1], newBlocks[1]);
        Assert.All(Enumerable.Range(2, oldBlocks.Count - 2), i => Assert.Same(oldBlocks[i], newBlocks[i]));
        Assert.Same(before.Map[host.Id("first")], after.Map[host.Id("first")]);
    }

    [Fact]
    public void CaretMovesWithoutActivationChangesReuseTheBlock()
    {
        var host = EditorTestHost.Load("<#p>plain text <|>here **bold**\n\nother");
        var before = host.Project();
        host.Press("Right");
        var after = host.Project();
        Assert.Same(ProjectionDump.Blocks(before)[0], ProjectionDump.Blocks(after)[0]);
        Assert.NotEqual(before.Selection, after.Selection);
        Assert.Equal(0, after.Statistics.BlocksProjected);
    }

    [Fact]
    public void ActivationChangesReprojectOnlyTheFocusedBlocks()
    {
        var host = EditorTestHost.Load("<#a>x **a**<|>\n\n<#b>y **b**\n\n<#c>z");
        host.Project();
        host.Execute(new SetSelection(TextSelection.Caret(host.Id("b"), 3)));
        var after = host.Project();
        Assert.Equal(2, after.Statistics.BlocksProjected);
        Assert.Equal([InlineKind.Strong], after.Active.ActiveNodes.Select(static node => node.Kind));
    }

    [Fact]
    public void PointerDownFreezesActivation()
    {
        var host = EditorTestHost.Load("<#a>x **a**<|>\n\n<#b>y **b**");
        var before = host.Project();
        host.Execute(new SetInteraction(host.State.Interaction with { IsPointerDown = true }));
        host.Execute(new SetSelection(TextSelection.Caret(host.Id("b"), 3)));
        var during = host.Project();
        Assert.Equal(before.Active.FocusedBlock, during.Active.FocusedBlock);
        Assert.Same(ProjectionDump.Blocks(before)[1], ProjectionDump.Blocks(during)[1]);

        host.Execute(new SetInteraction(host.State.Interaction with { IsPointerDown = false }));
        Assert.Equal(host.Id("b"), host.Project().Active.FocusedBlock);
    }

    [Theory]
    [InlineData("Type")]
    [InlineData("Enter")]
    [InlineData("Backspace")]
    [InlineData("Tab")]
    [InlineData("Ctrl+Z")]
    public void EditsAndUndoDoNoFullProjection(string action)
    {
        var host = EditorTestHost.Load("para\n\n- a\n- b<|>\n\n> q\n\n[^1]: n\n\n[r]: /u");
        host.Project();
        host.Type("x");
        host.Project();
        using var scope = OperationCounters.BeginScope();
        if (action == "Type")
        {
            host.Type("y");
        }
        else
        {
            host.Press(action);
        }

        host.Project();
        Assert.Equal(0, scope.FullProjections);
        Assert.Equal(0, scope.FullDocumentParses);
        Assert.Equal(0, scope.FullDocumentSerializations);
    }

    [Fact]
    public void DefiningALabelReparsesTheBlocksThatUseIt()
    {
        var host = EditorTestHost.Load("<#p>see [foo] here\n\n<#q>unrelated\n\n<#d>[fo<|>]: /url");
        var before = host.Project();
        Assert.DoesNotContain(Runs(before, host, "p"), static run => run.Style.HasFlag(InlineStyle.Link));

        host.Type("o");
        var after = host.Project();
        Assert.Contains(Runs(after, host, "p"), static run => run.Style.HasFlag(InlineStyle.Link));
        Assert.Same(ProjectionDump.Find(before, DocumentProjector.KeyOf(host.Id("q"))), ProjectionDump.Find(after, DocumentProjector.KeyOf(host.Id("q"))));

        host.Press("Backspace");
        Assert.DoesNotContain(Runs(host.Project(), host, "p"), static run => run.Style.HasFlag(InlineStyle.Link));
    }

    private static IEnumerable<InlineRun> Runs(ProjectionResult result, EditorTestHost host, string name) =>
        ((InlineContent)ProjectionDump.Attribute(ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id(name))), ProjectionNames.Content)!).Runs;
}
