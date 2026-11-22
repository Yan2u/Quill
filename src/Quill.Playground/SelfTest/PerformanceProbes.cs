using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Input;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Input;
using Quill.Playground.Samples;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.6: keystroke cost on the benchmark documents of 09 (opt-in: <c>-Only Performance</c>). Each
/// document is opened in a <see cref="MarkdownBox"/>, the caret goes to a block in its middle, and a
/// burst of typing (characters, Backspace, Enter) runs through the box's entry points. For every key
/// the UI-thread work is the synchronous update (command, projection, reconcile, layout, caret into
/// view, input method notification) plus the drawing of the frames that follow it. Target: p95 &lt; 8 ms (09).
/// </summary>
internal static class PerformanceProbes
{
    private const double TargetMilliseconds = 8;

    public static async Task RunAsync(SelfTestContext context)
    {
        if (BenchmarkDocuments.EnsureWritten() is not { } folder)
        {
            context.Info("benchmark documents: not in the repository, skipped");
            return;
        }

        context.Info($"configuration: {(Debugger.IsAttached ? "debugger attached, " : string.Empty)}{BuildConfiguration()}");
        await WarmUpAsync(context);
        foreach (var (name, _) in BenchmarkDocuments.All)
        {
            context.Stage.Children.Clear();
            var box = new MarkdownBox
            {
                Width = 900,
                Height = 800,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Clipboard = new MemoryClipboard(),
            };
            context.Stage.Children.Add(box);
            await Task.Delay(50);
            await MeasureAsync(context, box, Path.Combine(folder, name));
        }

        await MeasureSourceAsync(context, Path.Combine(folder, "long-10000.md"));
        context.Stage.Children.Clear();
    }

    /// <summary>
    /// The source mode on the longest document (11-source-mode.md): switching both ways, typing in a line
    /// in the middle, and how long the background highlighting of the whole text takes.
    /// </summary>
    private static async Task MeasureSourceAsync(SelfTestContext context, string path)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox { Width = 900, Height = 800, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Clipboard = new MemoryClipboard() };
        context.Stage.Children.Add(box);
        await Task.Delay(50);
        var ids = new SequentialBlockIdSource();
        var document = (await MarkdownFile.OpenAsync(path, ids)).Document;
        var (target, _) = Target(document);
        box.Load(new EditorState { Document = document, Selection = TextSelection.Caret(target) }, ids);
        await SettleAsync();

        var watch = Stopwatch.StartNew();
        box.ToggleSourceMode();
        double enter = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        while (box.Highlighting is { IsComplete: false } && watch.Elapsed < TimeSpan.FromSeconds(60))
        {
            await Task.Delay(50);
        }

        double highlighted = watch.Elapsed.TotalMilliseconds;
        await SettleAsync();
        var total = new List<double>();
        foreach (char c in string.Concat(Enumerable.Repeat("source typing ", 8)))
        {
            var drawn = MdBlockView.TotalDrawTime;
            long start = Stopwatch.GetTimestamp();
            box.HandleCharacter(c);
            double synchronous = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            await FramesAsync(2);
            total.Add(synchronous + (MdBlockView.TotalDrawTime - drawn).TotalMilliseconds);
        }

        total.Sort();
        watch.Restart();
        box.ToggleSourceMode();
        double leave = watch.Elapsed.TotalMilliseconds;
        context.Check(Percentile(total, 0.95) < TargetMilliseconds,
            $"performance source mode, {Path.GetFileName(path)}: keystroke p95 < {TargetMilliseconds} ms (09, 11)",
            $"enter {enter:F0} ms, highlighting in the background {highlighted:F0} ms, leave {leave:F0} ms; {total.Count} keys: p50 {Percentile(total, 0.5):F2} / p95 {Percentile(total, 0.95):F2} / max {total[^1]:F2} ms");
    }

    /// <summary>
    /// Types the same burst into the showcase once, unmeasured: the first keys after start compile the
    /// code they run (JIT) and would otherwise land in the first document's numbers.
    /// </summary>
    private static async Task WarmUpAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var box = new MarkdownBox { Width = 900, Height = 800, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Clipboard = new MemoryClipboard() };
        context.Stage.Children.Add(box);
        await Task.Delay(50);
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.Import(MarkdownShowcase.Text, ids).Document;
        box.Load(EditorState.AtStart(document), ids);
        await SettleAsync();
        var (target, keys) = Target(document);
        box.Execute(new SetSelection(TextSelection.Caret(target)));
        foreach (var key in keys)
        {
            key(box);
            await FramesAsync(1);
        }
    }

    private static async Task MeasureAsync(SelfTestContext context, MarkdownBox box, string path)
    {
        string name = Path.GetFileName(path);
        var open = Stopwatch.StartNew();
        var ids = new SequentialBlockIdSource();
        var document = (await MarkdownFile.OpenAsync(path, ids)).Document;
        double import = open.Elapsed.TotalMilliseconds;
        box.Load(EditorState.AtStart(document), ids);
        double load = open.Elapsed.TotalMilliseconds;
        await SettleAsync();

        var (target, keys) = Target(document);
        box.Execute(new SetSelection(TextSelection.Caret(target)));
        await SettleAsync();

        var sync = new List<double>();
        var total = new List<double>();
        var reports = new List<EditorUpdateReport>();
        var burstDrawn = MdBlockView.TotalDrawTime;
        var slow = new List<string>();
        var allocations = new List<(string report, long bytes)>();
        foreach (var key in keys)
        {
            var drawn = MdBlockView.TotalDrawTime;
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var paused = GC.GetTotalPauseDuration();
            long start = Stopwatch.GetTimestamp();
            key(box);
            double synchronous = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            allocations.Add((report: box.LastUpdate?.Command ?? "?", bytes: GC.GetAllocatedBytesForCurrentThread() - allocated));
            if (box.LastUpdate is { } report)
            {
                reports.Add(report);
                if (synchronous > TargetMilliseconds)
                {
                    slow.Add($"{report.Command} {synchronous:F1} ms (GC {GC.CollectionCount(0) - gen0}/{GC.CollectionCount(1) - gen1}/{GC.CollectionCount(2) - gen2}, paused {(GC.GetTotalPauseDuration() - paused).TotalMilliseconds:F1} ms)");
                }
            }

            await FramesAsync(2);
            sync.Add(synchronous);
            total.Add(synchronous + (MdBlockView.TotalDrawTime - drawn).TotalMilliseconds);
        }

        await SettleAsync();
        double drawPerKey = (MdBlockView.TotalDrawTime - burstDrawn).TotalMilliseconds / keys.Count;
        string Mean(Func<EditorUpdateReport, TimeSpan> phase) => reports.Count == 0 ? "-" : reports.Average(report => phase(report).TotalMilliseconds).ToString("F2");
        context.Info($"{Path.GetFileName(path)}: allocated per key: "
            + string.Join(", ", allocations.GroupBy(item => item.report).Select(group => $"{group.Key} {group.Average(item => item.bytes) / 1024:F0} KB")));
        context.Info($"{Path.GetFileName(path)}: keys over {TargetMilliseconds} ms (GC gen0/gen1/gen2 during the key): {(slow.Count == 0 ? "none" : string.Join(", ", slow))}");
        context.Info($"{Path.GetFileName(path)}: mean phases: command {Mean(r => r.CommandTime)}, projection {Mean(r => r.ProjectionTime)}, reconcile {Mean(r => r.ReconcileTime)}, layout {Mean(r => r.LayoutTime)}, scroll {Mean(r => r.ScrollTime)}, input {Mean(r => r.InputTime)}; drawing {drawPerKey:F2} ms per key; slowest: {reports.MaxBy(report => report.Total)}");

        sync.Sort();
        total.Sort();
        context.Check(Percentile(total, 0.95) < TargetMilliseconds,
            $"performance {name}: keystroke p95 < {TargetMilliseconds} ms (09)",
            $"{document.Children.Count} top-level blocks, {new FileInfo(path).Length / 1024} KB; open {import:F0} ms + first render {load - import:F0} ms; "
            + $"{keys.Count} keys in {Describe(document, target)}: total p50 {Percentile(total, 0.5):F2} / p95 {Percentile(total, 0.95):F2} / max {total[^1]:F2} ms; "
            + $"synchronous p50 {Percentile(sync, 0.5):F2} / p95 {Percentile(sync, 0.95):F2} / max {sync[^1]:F2} ms");
    }

    /// <summary>A text position in the middle of the document, and the keys to type there.</summary>
    private static (TextPosition Target, List<Action<MarkdownBox>> Keys) Target(Document document)
    {
        var leaves = new List<LeafBlock>();
        Collect(document, leaves);
        var code = leaves.OfType<CodeBlock>().Where(block => block.Text.Length > 10000).ToList();
        var cells = leaves.OfType<TableCell>().ToList();
        LeafBlock leaf;
        int offset;
        bool enter = true;
        if (code.Count > 0)
        {
            // The middle line of the second large code block.
            leaf = code[System.Math.Min(1, code.Count - 1)];
            offset = leaf.Text.IndexOf('\n', leaf.Text.Length / 2);
        }
        else if (cells.Count > 50)
        {
            leaf = cells[cells.Count / 2];
            offset = leaf.Text.Length;
            enter = false;
        }
        else
        {
            var paragraphs = leaves.OfType<Paragraph>().Where(paragraph => paragraph.Text.Length > 40).ToList();
            leaf = paragraphs[paragraphs.Count / 2];
            offset = leaf.Text.IndexOf(' ', leaf.Text.Length / 2);
        }

        var keys = new List<Action<MarkdownBox>>();
        const string text = "quill types here ";
        for (int i = 0; i < 120; i++)
        {
            char c = text[i % text.Length];
            keys.Add(box => box.HandleCharacter(c));
            if (i % 30 == 29)
            {
                for (int b = 0; b < 5; b++)
                {
                    keys.Add(box => box.HandleKey(0x08, KeyModifiers.None));
                }

                if (enter)
                {
                    keys.Add(box => box.HandleKey(0x0D, KeyModifiers.None));
                }
            }
        }

        return (new TextPosition(leaf.Id, System.Math.Max(0, offset)), keys);
    }

    private static void Collect(Block block, List<LeafBlock> leaves)
    {
        if (block is LeafBlock leaf)
        {
            leaves.Add(leaf);
        }
        else if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                Collect(child, leaves);
            }
        }
    }

    private static string Describe(Document document, TextPosition target) =>
        DocumentLookup.Find(document, target.Block) is { } block ? $"a {block.GetType().Name.ToLowerInvariant()} ({((LeafBlock)block).Text.Length} chars)" : "?";

    private static double Percentile(List<double> sorted, double fraction) =>
        sorted.Count == 0 ? 0 : sorted[System.Math.Min(sorted.Count - 1, (int)System.Math.Ceiling(fraction * sorted.Count) - 1)];

    private static async Task SettleAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            await FramesAsync(2);
            await Task.Delay(30);
        }
    }

    /// <summary>Waits for the next frames, so the drawing caused by a key happens before the next key.</summary>
    private static Task FramesAsync(int count)
    {
        var done = new TaskCompletionSource();
        int seen = 0;
        EventHandler<object>? handler = null;
        handler = (_, _) =>
        {
            if (++seen >= count)
            {
                CompositionTarget.Rendering -= handler;
                done.TrySetResult();
            }
        };
        CompositionTarget.Rendering += handler;
        return done.Task;
    }

    private static string BuildConfiguration()
    {
#if DEBUG
        return "Debug build";
#else
        return "Release build";
#endif
    }
}
