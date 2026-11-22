using System.Diagnostics;
using System.Text;
using Quill.Editor.Commands;
using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Xunit;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// 09-performance.md on a long document: a keystroke projects one block, renders one leaf and does no
/// full parse, serialization or projection. The timings go to the test output for reference only.
/// </summary>
public sealed class LargeDocumentProjectionTests(ITestOutputHelper output)
{
    [Fact]
    public void TypingInALongDocumentStaysLocal()
    {
        var markdown = new StringBuilder();
        for (int i = 0; i < 2500; i++)
        {
            markdown.Append((i % 4) switch
            {
                0 => $"# Heading {i}\n\n",
                1 => $"Paragraph {i} with **bold**, *em*, `code`, [link](u) and $x^{i}$.\n\n",
                2 => $"- item {i}\n- item [^n] two\n\n",
                _ => $"> quote {i}\n\n",
            });
        }

        markdown.Append("[^n]: note\n");
        var document = MarkdownImporter.Import(markdown.ToString(), new SequentialBlockIdSource()).Document;
        var paragraph = Navigation.Leaves(document).OfType<Paragraph>().Skip(1200).First();
        var session = new EditorSession(
            new EditorState { Document = document, Selection = TextSelection.Caret(paragraph.Id, 10) },
            new EditorServices { Ids = new SequentialBlockIdSource(1_000_000) });

        var clock = Stopwatch.StartNew();
        var first = session.Project();
        output.WriteLine($"First projection of {document.Children.Count} top-level blocks: {clock.Elapsed.TotalMilliseconds:F1} ms");
        Assert.True(first.Statistics.IsFull);

        var times = new List<double>();
        var edits = new List<double>();
        using var scope = OperationCounters.BeginScope();
        for (int i = 0; i < 200; i++)
        {
            clock.Restart();
            session.Execute(new InsertText(i % 10 == 0 ? "*" : "a"));
            edits.Add(clock.Elapsed.TotalMilliseconds);
            var result = session.Project();
            times.Add(clock.Elapsed.TotalMilliseconds);
            Assert.Equal(1, result.Statistics.BlocksProjected);
            Assert.Equal(1, result.Statistics.LeavesRendered);
        }

        Assert.Equal(0, scope.FullProjections);
        Assert.Equal(0, scope.FullDocumentParses);
        Assert.Equal(0, scope.FullDocumentSerializations);
        times.Sort();
        edits.Sort();
        output.WriteLine($"Keystroke (command only): median {edits[edits.Count / 2]:F3} ms");
        output.WriteLine($"Keystroke + projection: median {times[times.Count / 2]:F3} ms, p95 {times[(int)(times.Count * 0.95)]:F3} ms");
    }

    /// <summary>
    /// Enter and Backspace at a paragraph's start add and remove a top-level block: only the blocks
    /// they touch are projected, and the unchanged ones keep their nodes by reference (M5.6).
    /// </summary>
    [Fact]
    public void SplittingAndJoiningTopLevelBlocksProjectOnlyThem()
    {
        var markdown = new StringBuilder();
        for (int i = 0; i < 3000; i++)
        {
            markdown.Append($"Paragraph {i} with some text.\n\n");
        }

        var document = MarkdownImporter.Import(markdown.ToString(), new SequentialBlockIdSource()).Document;
        var paragraph = (Paragraph)document.Children[1500];
        var session = new EditorSession(
            new EditorState { Document = document, Selection = TextSelection.Caret(paragraph.Id, 9) },
            new EditorServices { Ids = new SequentialBlockIdSource(1_000_000) });
        var before = session.Project().Root.Children[0].Children;
        int count = before.Count;

        session.Execute(new SplitBlock());
        var split = session.Project();
        var stack = split.Root.Children[0].Children;
        Assert.Equal(count + 1, stack.Count);
        Assert.InRange(split.Statistics.BlocksProjected, 1, 2);
        Assert.Same(before[1499], stack[1499]);
        Assert.Same(before[1501], stack[1502]);
        Assert.Equal(" 1500 with some text.", ((Paragraph)session.State.Document.Children[1501]).Text);

        session.Execute(new DeleteBackward());
        var joined = session.Project();
        stack = joined.Root.Children[0].Children;
        Assert.Equal(count, stack.Count);
        Assert.Equal(1, joined.Statistics.BlocksProjected);
        Assert.Same(before[1501], stack[1501]);
        Assert.False(joined.Statistics.IsFull);

        // The result is what a projection from scratch gives.
        var fresh = new DocumentProjector().Project(session.State).Root;
        Assert.Equal(fresh.Children[0].Children.Count, stack.Count);
        Assert.Equal(fresh.Hash, joined.Root.Hash);
    }
}
