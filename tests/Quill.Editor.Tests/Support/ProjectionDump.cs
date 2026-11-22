using Quill.Editor.Projection;
using Quill.VariableXaml.Markup;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Compact text forms of projection output: runs as <c>"text"</c> (styled: <c>Strong"b"</c>), markers as
/// <c>&lt;**&gt;</c>, line breaks as <c>NL</c> and objects as <c>Math[x]</c>; segments as
/// <c>Kind source&gt;display</c>.
/// </summary>
internal static class ProjectionDump
{
    public static string Runs(InlineContent content) => string.Join(" ", content.Runs.Select(Run));

    public static string Run(InlineRun run)
    {
        string style = run.Style == InlineStyle.None ? string.Empty : run.Style.ToString().Replace(", ", "+", StringComparison.Ordinal);
        return run.Role switch
        {
            RunRole.Text => $"{style}\"{run.Text}\"",
            RunRole.Marker => $"{style}<{run.Text}>",
            RunRole.LineBreak => "NL",
            _ => $"{run.Role}[{run.Text}]",
        };
    }

    public static string Segments(BlockPositionMap map) => string.Join(" ", map.Segments.Select(static segment =>
        $"{segment.Kind.ToString()[0]}{segment.Source}>{segment.Display}"));

    public static string Markup(VNode node) => VNodeMarkupWriter.Write(node, static value => value switch
    {
        InlineContent content => Runs(content),
        _ => null,
    });

    /// <summary>The block nodes under the document's virtual stack.</summary>
    public static IReadOnlyList<VNode> Blocks(ProjectionResult result) => result.Root.Children[0].Children;

    public static VNode Find(ProjectionResult result, string key) => Find(result.Root, key) ?? throw new KeyNotFoundException(key);

    public static object? Attribute(VNode node, string name) => node.GetAttribute(XamlMemberName.Plain(name));

    private static VNode? Find(VNode node, string key)
    {
        if (node.Key == key)
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            if (Find(child, key) is { } found)
            {
                return found;
            }
        }

        return null;
    }
}
