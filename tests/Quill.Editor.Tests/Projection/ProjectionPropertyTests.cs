using System.Text;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// 08-testing-and-debugging.md: incremental projection equals full projection, after every step of
/// random editing (typing syntax, keys, pastes, selections, pointer freezes, undo and redo). The
/// document's reference index, maintained incrementally, must equal a fresh one too.
/// </summary>
public sealed class ProjectionPropertyTests
{
    private const int Steps = 120;

    private static readonly string[] Starts =
    [
        "<|>",
        "# Title **bold**<|>\n\nSome *text* [link](u \"t\") and [ref] and [^1] here.\n\n- one $x$\n- two ![i](p.png)\n    - nested :smile:\n\n> quote &copy;\n\n```js\ncode\n```\n\n| a | **b** |\n| :- | -: |\n| 1 | 2 |\n\n[^1]: note **n**\n\n[ref]: http://x \"t\"\n\n***\n\n1. a\n2. b",
        "a <u>u</u> <br> `c` ==h== ~s~ ^p^ ~~d~~<|>\n\n[x]: /x\n\n[foo][x] [x][] [x]\n\n$$\nm\n$$",
        "[^a] [^a] [^b]<|>\n\n[^a]: first\n\n[^a]: second\n\n[^b]: other",
    ];

    private static readonly string[] Texts =
    [
        "a", "b", " ", "**", "*", "_", "`", "$", "[", "]", "(u)", "[^1]", "[^a]", "![", ":smile:", "&amp;", "<u>", "</u>", "<br>",
        "\\", "~", "^", "==", "\n", "x", "ref", "中文", "[x]", "]: /u",
    ];

    private static readonly string[] Keys =
    [
        "Enter", "Shift+Enter", "Tab", "Shift+Tab", "Backspace", "Backspace", "Delete", "Left", "Right", "Up", "Down",
        "Home", "End", "Shift+Left", "Shift+Right", "Ctrl+A", "Ctrl+Z", "Ctrl+Y", "Ctrl+X", "Ctrl+V",
    ];

    public static TheoryData<int> Seeds
    {
        get
        {
            string? range = Environment.GetEnvironmentVariable("QUILL_PROPERTY_SEEDS");
            if (range?.Split('-') is [var from, var to] && int.TryParse(from, out int first) && int.TryParse(to, out int last))
            {
                return [.. Enumerable.Range(first, last - first + 1)];
            }

            return [.. Enumerable.Range(1, 24)];
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void IncrementalProjectionEqualsFullProjection(int seed)
    {
        var random = new Random(seed);
        var host = EditorTestHost.Load(Starts[seed % Starts.Length]);
        host.Project();
        var log = new StringBuilder();
        for (int step = 0; step < Steps; step++)
        {
            log.Append(Act(host, random)).Append(" | ");
            host.Advance(TimeSpan.FromMilliseconds(random.Next(0, 1500)));
            Compare(host, $"seed {seed}, step {step}: {log}");
        }
    }

    private static void Compare(EditorTestHost host, string context)
    {
        var state = host.State;
        var incremental = host.Project();
        var fresh = new DocumentProjector().Project(state);
        if (incremental.Root.Hash != fresh.Root.Hash)
        {
            Assert.Fail($"{context}\nIncremental:\n{ProjectionDump.Markup(incremental.Root)}\nFresh:\n{ProjectionDump.Markup(fresh.Root)}");
        }

        Assert.Equal(ProjectionDump.Markup(fresh.Root), ProjectionDump.Markup(incremental.Root));
        Assert.Equal(fresh.Map.Blocks.Count, incremental.Map.Blocks.Count);
        foreach (var (id, map) in fresh.Map.Blocks)
        {
            Assert.True(map.Equals(incremental.Map[id]), $"{context}\nThe position map of {id} differs: {ProjectionDump.Segments(map)} / {(incremental.Map[id] is { } other ? ProjectionDump.Segments(other) : "none")}");
        }

        var references = ReferenceIndex.Of(state.Document);
        Assert.True(references.HasSameEntries(ReferenceIndex.BuildFresh(state.Document)), $"{context}\nThe reference index differs from a fresh one.");
        CheckInvariants(state, fresh, context);
    }

    /// <summary>Invariants of a projection on its own (05-projection-and-view.md).</summary>
    private static void CheckInvariants(EditorState state, ProjectionResult result, string context)
    {
        var leaves = Navigation.Leaves(state.Document).ToList();
        Assert.Equal(leaves.Count, result.Map.Blocks.Count);
        foreach (var leaf in leaves)
        {
            var map = result.Map[leaf.Id]!;
            int source = 0;
            int display = 0;
            foreach (var segment in map.Segments)
            {
                Assert.True(segment.Source.Start == source && segment.Display.Start == display, $"{context}\nThe segments of {leaf.Id} leave a gap: {ProjectionDump.Segments(map)}");
                int expected = segment.Kind switch
                {
                    SegmentKind.Hidden => 0,
                    SegmentKind.Atomic or SegmentKind.LineBreak => 1,
                    _ => segment.Source.Length,
                };
                Assert.Equal(expected, segment.Display.Length);
                source = segment.Source.End;
                display = segment.Display.End;
            }

            Assert.Equal(leaf.Text.Length, source);
            if (leaf is TextLeafBlock && ProjectionDump.Find(result, DocumentProjector.KeyOf(leaf.Id)) is var node
                && ProjectionDump.Attribute(node, ProjectionNames.Content) is InlineContent content)
            {
                Assert.Equal(display, content.Runs.IsEmpty ? 0 : content.Runs[^1].Display.End);
            }
        }

        int gaps = Count(result.Root, static node => node.Type == ProjectionNames.VirtualParagraph);
        bool atBoundary = state.Selection.IsCollapsed && state.Selection.Head is BoundaryPosition;
        Assert.True(gaps == (atBoundary ? 1 : 0), $"{context}\n{gaps} virtual paragraphs for the selection {state.Selection}.");
    }

    private static int Count(VariableXaml.Model.VNode node, Func<VariableXaml.Model.VNode, bool> predicate) =>
        (predicate(node) ? 1 : 0) + node.Children.Sum(child => Count(child, predicate));

    private static string Act(EditorTestHost host, Random random)
    {
        switch (random.Next(12))
        {
            case < 5:
                string text = Texts[random.Next(Texts.Length)];
                if (text == "\n")
                {
                    host.Press("Shift+Enter");
                }
                else
                {
                    host.Type(text);
                }

                return $"type '{text}'";
            case < 9:
                string key = Keys[random.Next(Keys.Length)];
                host.Clipboard ??= new ClipboardContent(null, "[p](q) **r**");
                host.Press(key);
                return key;
            case 9:
                bool down = !host.State.Interaction.IsPointerDown;
                host.Execute(new SetInteraction(host.State.Interaction with { IsPointerDown = down }));
                return down ? "pointer down" : "pointer up";
            default:
                var leaves = Navigation.Leaves(host.State.Document).ToList();
                Position Pick()
                {
                    var leaf = leaves[random.Next(leaves.Count)];
                    return new TextPosition(leaf.Id, random.Next(leaf.Text.Length + 1));
                }

                var head = Pick();
                var selection = random.Next(3) == 0 ? new TextSelection(Pick(), head) : TextSelection.Caret(head);
                host.Execute(new SetSelection(selection));
                return $"select {selection}";
        }
    }
}
