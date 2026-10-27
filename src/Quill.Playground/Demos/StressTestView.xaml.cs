using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Playground.VariableXaml;

namespace Quill.Playground.Demos;

/// <summary>
/// Measures update and layout cost of typical editor operations on a large code-built document,
/// comparing incremental patching with full reloads and StackPanel with vx:VirtualStack.
/// </summary>
public sealed partial class StressTestView : UserControl
{
    private readonly Random _random = new(7);
    private readonly List<string> _results = [];
    private StressDocument? _document;
    private bool _busy;

    public StressTestView()
    {
        InitializeComponent();
        Host.DeferUpdates = false;
        Host.MeasureLayout = true;
    }

    private bool Virtualize => VirtualizeSwitch.IsOn;

    private string Mode => $"{(IncrementalSwitch.IsOn ? "incremental" : "full reload")}, {(Virtualize ? "VirtualStack" : "StackPanel")}, {_document?.Blocks.Count ?? 0:N0} blocks";

    private async void OnBuild(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _document = StressDocument.Generate((int)BlockCountBox.Value);
        Host.Strategy = IncrementalSwitch.IsOn ? UpdateStrategy.Incremental : UpdateStrategy.FullReload;
        Host.Reset();
        GC.Collect();
        long before = PrivateBytes();
        Host.Render(_document.Render(Virtualize));
        UpdateReport report = Host.LastReport!;
        await Task.Delay(200);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long after = PrivateBytes();
        AddResult($"[Build] {Mode}\n" +
            $"  mount {ReportFormatter.Milliseconds(report.ReconcileTime)}, first layout {ReportFormatter.Milliseconds(report.LayoutTime)}, " +
            $"{report.TreeNodeCount:N0} virtual nodes, {report.Statistics.XamlReaderLoads} XamlReader load(s)\n" +
            $"  private bytes {after / (1024 * 1024):N0} MB ({(after - before) / (1024 * 1024):+#,0;-#,0;0} MB vs. before the build)");
    });

    private async void OnType(object sender, RoutedEventArgs e) => await RunOperationAsync("Type in first paragraph", (document, i) =>
    {
        DocumentBlock block = document.Blocks.First(candidate => candidate.Kind == BlockKind.Paragraph);
        block.Text += i % 6 == 5 ? " " : "x";
        document.Touch(block);
    });

    private async void OnInsert(object sender, RoutedEventArgs e) => await RunOperationAsync("Insert one block", (document, _) =>
        document.Blocks.Insert(_random.Next(document.Blocks.Count + 1), document.CreateBlock(_random.Next(1000))));

    private async void OnDelete(object sender, RoutedEventArgs e) => await RunOperationAsync("Delete one block", (document, _) =>
    {
        if (document.Blocks.Count > 1)
        {
            document.Blocks.RemoveAt(_random.Next(document.Blocks.Count));
            document.PruneCache();
        }
    });

    private async void OnMove(object sender, RoutedEventArgs e) => await RunOperationAsync("Move one block", (document, _) =>
    {
        int from = _random.Next(document.Blocks.Count);
        DocumentBlock block = document.Blocks[from];
        document.Blocks.RemoveAt(from);
        document.Blocks.Insert(_random.Next(document.Blocks.Count + 1), block);
    });

    private async void OnRandomEdits(object sender, RoutedEventArgs e) => await RunOperationAsync("Random edit", (document, i) =>
    {
        DocumentBlock block = document.Blocks[_random.Next(document.Blocks.Count)];
        switch (_random.Next(5))
        {
            case 0:
                document.Blocks.Insert(_random.Next(document.Blocks.Count + 1), document.CreateBlock(_random.Next(1000)));
                break;
            case 1 when document.Blocks.Count > 1:
                document.Blocks.Remove(block);
                document.PruneCache();
                break;
            case 2:
                block.Kind = block.Kind == BlockKind.Paragraph ? BlockKind.Quote : BlockKind.Paragraph;
                document.Touch(block);
                break;
            default:
                block.Text = block.Text.Replace("editor", $"edit{i}");
                block.Text += " more";
                document.Touch(block);
                break;
        }
    });

    private void OnClearResults(object sender, RoutedEventArgs e)
    {
        _results.Clear();
        ResultsText.Text = string.Empty;
    }

    private async Task RunOperationAsync(string name, Action<StressDocument, int> mutate) => await RunAsync(async () =>
    {
        if (_document is null)
        {
            AddResult("Build a document first.");
            return;
        }

        Host.Strategy = IncrementalSwitch.IsOn ? UpdateStrategy.Incremental : UpdateStrategy.FullReload;
        int iterations = (int)IterationsBox.Value;
        var reports = new List<UpdateReport>(iterations);
        Progress.Maximum = iterations;
        for (int i = 0; i < iterations; i++)
        {
            mutate(_document, i);
            Host.Render(_document.Render(Virtualize));
            reports.Add(Host.LastReport!);
            Progress.Value = i + 1;
            if (i % 5 == 4)
            {
                // Let a frame render so the progress is visible; the measured work is synchronous.
                await Task.Delay(1);
            }
        }

        AddResult(Summarize(name, reports));
    });

    private async Task RunAsync(Func<Task> operation)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        BuildButton.IsEnabled = false;
        OperationButtons.IsHitTestVisible = false;
        try
        {
            await operation();
        }
        finally
        {
            _busy = false;
            BuildButton.IsEnabled = true;
            OperationButtons.IsHitTestVisible = true;
            Progress.Value = 0;
        }
    }

    private string Summarize(string name, List<UpdateReport> reports)
    {
        static string Stats(IEnumerable<double> values)
        {
            double[] sorted = values.Order().ToArray();
            double p95 = sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(sorted.Length * 0.95) - 1)];
            return $"avg {sorted.Average():F2} / p95 {p95:F2} / max {sorted[^1]:F2} ms";
        }

        double count = reports.Count;
        var builder = new StringBuilder();
        builder.AppendLine($"[{name} x{reports.Count}] {Mode}");
        builder.AppendLine($"  update {Stats(reports.Select(report => report.ReconcileTime.TotalMilliseconds))}");
        builder.AppendLine($"  layout {Stats(reports.Select(report => report.LayoutTime.TotalMilliseconds))}");
        builder.Append($"  per update: created {reports.Sum(r => r.Statistics.NodesCreated) / count:F1}, removed {reports.Sum(r => r.Statistics.NodesRemoved) / count:F1}, ")
            .Append($"patched {reports.Sum(r => r.Statistics.NodesPatched) / count:F1}, skipped {reports.Sum(r => r.Statistics.SubtreesSkipped) / count:F1}, ")
            .Append($"moved {reports.Sum(r => r.Statistics.NodesMoved) / count:F1}, props {reports.Sum(r => r.Statistics.PropertiesSet) / count:F1}, ")
            .Append($"text {reports.Sum(r => r.Statistics.TextUpdates) / count:F1}");
        VirtualizationSummary summary = Host.GetVirtualizationSummary();
        if (summary.Stacks > 0)
        {
            builder.Append($"\n  virtualized: {summary.Realized} of {summary.Items:N0} items realized");
        }

        return builder.ToString();
    }

    private void AddResult(string text)
    {
        _results.Insert(0, text);
        ResultsText.Text = string.Join("\n\n", _results);
    }

    private static long PrivateBytes()
    {
        using Process process = Process.GetCurrentProcess();
        return process.PrivateMemorySize64;
    }
}
