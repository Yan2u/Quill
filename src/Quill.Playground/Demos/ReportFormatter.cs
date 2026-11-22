using System.Text;
using Quill.VariableXaml;
using Quill.VariableXaml.Markup;
using Quill.VariableXaml.Reconciliation;

namespace Quill.Playground.Demos;

/// <summary>Formats update reports for the demo status bars.</summary>
internal static class ReportFormatter
{
    public static string Format(UpdateReport report, VirtualizationSummary summary)
    {
        var builder = new StringBuilder();
        builder.Append('#').Append(report.Sequence).Append("  ");
        if (report.ParseKind is { } kind)
        {
            builder.Append(kind switch
            {
                ParseKind.Incremental => $"parse: incremental <{report.ReparsedElement}> {report.ParsedLength:N0} of {report.DocumentLength:N0} chars",
                ParseKind.Full => $"parse: full {report.DocumentLength:N0} chars",
                _ => "parse: unchanged",
            });
            builder.Append(' ').Append(Milliseconds(report.ParseTime)).Append("   ");
        }

        builder.Append(report.Strategy == UpdateStrategy.Incremental ? "patch " : "full reload ").Append(Milliseconds(report.ReconcileTime));
        if (report.LayoutTime > TimeSpan.Zero)
        {
            builder.Append("   layout ").Append(Milliseconds(report.LayoutTime));
        }

        builder.AppendLine();
        UpdateStatistics statistics = report.Statistics;
        builder.Append($"nodes {report.TreeNodeCount:N0} | created {statistics.NodesCreated:N0}, removed {statistics.NodesRemoved:N0}, ")
            .Append($"patched {statistics.NodesPatched:N0}, skipped {statistics.SubtreesSkipped:N0}, moved {statistics.NodesMoved}, ")
            .Append($"replaced {statistics.NodesReplaced} | props set {statistics.PropertiesSet}, cleared {statistics.PropertiesCleared}, ")
            .Append($"text {statistics.TextUpdates} | XamlReader loads {statistics.XamlReaderLoads}");
        if (summary.Stacks > 0)
        {
            builder.Append($" | virtual items {summary.Items:N0}, realized {summary.Realized}");
        }

        foreach (string entry in report.Trace.Take(3))
        {
            builder.AppendLine().Append("  ").Append(entry);
        }

        return builder.ToString();
    }

    public static string Milliseconds(TimeSpan time) => $"{time.TotalMilliseconds:F2} ms";
}
