using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Projection;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// M5.5, layer 1: TOC entries from the incremental heading index (Q68), hover flyouts declared on the
/// document node (05, 10, Q29), and asynchronous render results dropped when they arrive stale (05).
/// </summary>
public sealed class AttachedContentTests
{
    [Fact]
    public void TheTocListsTheHeadingsInOrderAsShown()
    {
        var host = EditorTestHost.Load("[toc]\n\n# **Bold** title\n\ntext\n\n## Second\n\n> ### Quoted<|>");
        var toc = Toc(host.Project());
        Assert.Equal(["1 Bold title", "2 Second", "3 Quoted"], toc.Entries.Select(static entry => $"{entry.Level} {entry.Text}"));
    }

    [Fact]
    public void TheTocFollowsHeadingEditsOnly()
    {
        var host = EditorTestHost.Load("[toc]\n\n# One\n\nbody<|>\n\n## Two");
        var first = host.Project();
        var before = ProjectionDump.Blocks(first)[0];

        // Typing in a paragraph leaves the TOC node as it was: one block projected.
        host.Type("x");
        var typed = host.Project();
        Assert.Same(before, ProjectionDump.Blocks(typed)[0]);
        Assert.Equal(1, typed.Statistics.BlocksProjected);

        // Typing in a heading updates the TOC.
        host.Execute(new SetSelection(TextSelection.Caret(host.State.Document.Children[1].Id, 3)));
        host.Type("!");
        var heading = host.Project();
        Assert.Equal(["One!", "Two"], Toc(heading).Entries.Select(static entry => entry.Text));

        // A new heading appears in its place.
        host.Execute(new SetSelection(TextSelection.Caret(host.State.Document.Children[2].Id, 5)));
        host.Press("Enter").Type("## Middle").Press("Enter");
        Assert.Equal(["One!", "Middle", "Two"], Toc(host.Project()).Entries.Select(static entry => entry.Text));
    }

    [Fact]
    public void TheHeadingIndexIsDerivedNotRebuilt()
    {
        var host = EditorTestHost.Load("# A\n\nb<|>");
        var index = HeadingIndex.Of(host.State.Document);
        host.Type("c");
        var after = HeadingIndex.Of(host.State.Document);
        Assert.Equal(index.Version, after.Version);
        host.Execute(new SetSelection(TextSelection.Caret(host.State.Document.Children[0].Id, 1)));
        host.Type("z");
        Assert.NotEqual(index.Version, HeadingIndex.Of(host.State.Document).Version);
    }

    [Theory]
    [InlineData("a[^1]<|> b\n\n[^1]: The note.", 2, "The note.", false)]
    [InlineData("a[^x]<|> b", 2, "Footnote [^x] is not defined.", true)]
    [InlineData("a [link](https://example.com \"Title\")<|>", 4, "Title", false)]
    [InlineData("a [link][r]<|>\n\n[r]: https://example.com \"Ref title\"", 4, "Ref title", false)]
    public void HoveringAnElementDeclaresItsFlyout(string dsl, int offset, string text, bool warning)
    {
        var host = EditorTestHost.Load(dsl);
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Project();
        string? target = HoverTargets.At(host.State, new TextPosition(block, offset));
        Assert.NotNull(target);
        host.Execute(new SetInteraction(host.State.Interaction with { HoverTarget = target }));
        var result = host.Project();
        var hover = Assert.IsType<HoverInfo>(ProjectionDump.Attribute(result.Root, ProjectionNames.Hover));
        Assert.Equal((text, warning), (hover.Text, hover.IsWarning));
        Assert.Equal(DocumentProjector.KeyOf(block), hover.BlockKey);
        Assert.True(hover.DisplayEnd > hover.DisplayStart);
        Assert.Equal(0, result.Statistics.BlocksProjected);
    }

    [Fact]
    public void PlainTextAndLinksWithoutTitlesShowNoFlyout()
    {
        var host = EditorTestHost.Load("plain [link](https://example.com)<|>");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        Assert.Null(HoverTargets.At(host.State, new TextPosition(block, 2)));
        Assert.Null(HoverTargets.At(host.State, new TextPosition(block, 8)));
    }

    [Fact]
    public void AnElementThatIsGoneDropsItsFlyout()
    {
        var host = EditorTestHost.Load("a[^1] b<|>\n\n[^1]: n");
        var block = ((TextPosition)host.State.Selection.Head).Block;
        host.Execute(new SetInteraction(host.State.Interaction with { HoverTarget = HoverTargets.At(host.State, new TextPosition(block, 2)) }));
        Assert.NotNull(ProjectionDump.Attribute(host.Project().Root, ProjectionNames.Hover));
        host.Execute(new SetSelection(TextSelection.Caret(block, 0)));
        host.Press("Delete Delete");
        Assert.Null(ProjectionDump.Attribute(host.Project().Root, ProjectionNames.Hover));
    }

    [Fact]
    public void RenderResultsAreKeptByContent()
    {
        var host = EditorTestHost.Load("![a](x.png)<|>");
        var scheduler = new ManualRenderScheduler();
        var cache = new RenderCache(scheduler, () => host.State.Document);
        var block = ((TextPosition)host.State.Selection.Head).Block;
        var ready = new List<BlockId>();
        cache.ResultReady += ready.Add;
        Assert.True(cache.Request(block, "image", "x.png", _ => Task.FromResult<object?>("decoded")));
        Assert.False(cache.Request(block, "image", "x.png", _ => Task.FromResult<object?>("again")));
        scheduler.RunPending();
        Assert.True(cache.TryGet("image", "x.png", out var result));
        Assert.Equal("decoded", result);
        Assert.Equal([block], ready);
        Assert.False(cache.Request(block, "image", "x.png", _ => Task.FromResult<object?>("again")));
    }

    [Fact]
    public void AResultForABlockThatChangedMeanwhileIsDropped()
    {
        var host = EditorTestHost.Load("![a](x.png)<|>");
        var scheduler = new ManualRenderScheduler();
        var cache = new RenderCache(scheduler, () => host.State.Document);
        var block = ((TextPosition)host.State.Selection.Head).Block;
        cache.Request(block, "image", "x.png", _ => Task.FromResult<object?>("decoded"));
        host.Type(" more");
        scheduler.RunPending();
        Assert.Equal(1, cache.Dropped);
        Assert.False(cache.TryGet("image", "x.png", out _));

        // The view asks again for the block's new revision, and that result is kept.
        Assert.True(cache.Request(block, "image", "x.png", _ => Task.FromResult<object?>("decoded")));
        scheduler.RunPending();
        Assert.True(cache.TryGet("image", "x.png", out _));
    }

    private static TocContent Toc(ProjectionResult result) =>
        Assert.IsType<TocContent>(ProjectionDump.Attribute(ProjectionDump.Blocks(result).First(node => node.Type == ProjectionNames.Toc), ProjectionNames.Entries));
}

/// <summary>A render scheduler the test advances by hand (08: <c>RunPendingRenders</c>).</summary>
public sealed class ManualRenderScheduler : IRenderScheduler
{
    private readonly Queue<(Func<CancellationToken, Task<object?>> Work, Action<object?> Completed)> _queue = new();

    public void Schedule(Func<CancellationToken, Task<object?>> work, Action<object?> completed) => _queue.Enqueue((work, completed));

    public void RunPending()
    {
        while (_queue.TryDequeue(out var job))
        {
            job.Completed(job.Work(CancellationToken.None).GetAwaiter().GetResult());
        }
    }
}
