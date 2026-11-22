using System.Text;
using Quill.Editor.Inlines;

namespace Quill.Editor.Tests.Support;

/// <summary>
/// Compact text form of inline trees for assertions: text as <c>"..."</c>, other nodes as
/// <c>Kind(opening|children or content|closing)</c>; empty syntax parts are left out.
/// </summary>
internal static class InlineDump
{
    public static string Dump(InlineTree tree) => string.Join(" ", tree.Nodes.Select(node => Dump(tree.Source, node)));

    public static string Dump(string source, InlineNode node)
    {
        if (node.Kind == InlineKind.Text)
        {
            return Quote(source, node.Range);
        }

        var builder = new StringBuilder(node.Kind.ToString()).Append('(');
        var parts = new List<string>();
        if (node.Opening.Length > 0)
        {
            parts.Add(Quote(source, node.Opening));
        }

        if (node.Children.Length > 0)
        {
            parts.Add(string.Join(" ", node.Children.Select(child => Dump(source, child))));
        }
        else if (node.Content.Length > 0)
        {
            parts.Add(Quote(source, node.Content));
        }

        if (node.Closing.Length > 0)
        {
            parts.Add(Quote(source, node.Closing));
        }

        return builder.Append(string.Join("|", parts)).Append(')').ToString();
    }

    /// <summary>Asserts that the nodes tile the source and every node's children tile its content.</summary>
    public static void AssertTiles(InlineTree tree)
    {
        AssertTiles(tree.Source, tree.Nodes, new TextRange(0, tree.Source.Length));
    }

    private static void AssertTiles(string source, IReadOnlyList<InlineNode> nodes, TextRange range)
    {
        int position = range.Start;
        foreach (var node in nodes)
        {
            Assert.True(node.Range.Start == position, $"Gap or overlap at {position} before {node} in \"{source}\".");
            Assert.True(node.Range.Start <= node.Content.Start && node.Content.Start <= node.Content.End && node.Content.End <= node.Range.End, $"Bad content range in {node} of \"{source}\".");
            if (node.Children.Length > 0)
            {
                AssertTiles(source, node.Children, node.Content);
            }

            position = node.Range.End;
        }

        Assert.True(position == range.End, $"The nodes end at {position}, not at {range.End}, in \"{source}\".");
    }

    private static string Quote(string source, TextRange range) =>
        "\"" + source[range.Start..range.End].Replace("\n", "\\n") + "\"";
}
