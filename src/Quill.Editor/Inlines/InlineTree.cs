using System.Collections.Immutable;
using Quill.Editor.Documents;

namespace Quill.Editor.Inlines;

/// <summary>
/// The inline syntax tree of a text leaf's source. It is derived from the source (never stored in the
/// document); its top-level <see cref="Nodes"/> tile the whole source without gaps, which the position
/// map relies on (06-markdown-io.md).
/// </summary>
public sealed class InlineTree
{
    public InlineTree(string source, InlineSyntaxSet syntax, ImmutableArray<InlineNode> nodes, ImmutableArray<string> referenceCandidates, ImmutableArray<string> definedReferences)
    {
        Source = source;
        Syntax = syntax;
        Nodes = nodes;
        ReferenceCandidates = referenceCandidates;
        DefinedReferences = definedReferences;
    }

    public string Source { get; }

    public InlineSyntaxSet Syntax { get; }

    public ImmutableArray<InlineNode> Nodes { get; }

    /// <summary>
    /// Normalized labels that could name a link reference definition (bracketed text in the source).
    /// The parse depends on which of them are defined, and on nothing else in the document.
    /// </summary>
    public ImmutableArray<string> ReferenceCandidates { get; }

    /// <summary>The candidates that were defined when the source was parsed.</summary>
    public ImmutableArray<string> DefinedReferences { get; }

    /// <summary>Whether parsing again with <paramref name="isDefined"/> would give the same tree.</summary>
    public bool IsValidFor(Func<string, bool>? isDefined)
    {
        foreach (string label in ReferenceCandidates)
        {
            if ((isDefined?.Invoke(label) ?? false) != DefinedReferences.Contains(label))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// The nodes whose ranges contain <paramref name="offset"/>, ends included, outermost first: the
    /// nodes an activation head at that offset expands (05-projection-and-view.md). Adjacent nodes that
    /// share the offset are all included.
    /// </summary>
    public IReadOnlyList<InlineNode> NodesTouching(int offset)
    {
        var result = new List<InlineNode>();
        Collect(Nodes, offset, result);
        return result;
    }

    /// <summary>All nodes in source order, parents before children.</summary>
    public IEnumerable<InlineNode> Descendants()
    {
        var stack = new Stack<InlineNode>();
        for (int i = Nodes.Length - 1; i >= 0; i--)
        {
            stack.Push(Nodes[i]);
        }

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (int i = node.Children.Length - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private static void Collect(ImmutableArray<InlineNode> nodes, int offset, List<InlineNode> result)
    {
        foreach (var node in nodes)
        {
            if (node.Range.Start > offset)
            {
                break;
            }

            if (node.Range.ContainsInclusive(offset))
            {
                result.Add(node);
                Collect(node.Children, offset, result);
            }
        }
    }
}
