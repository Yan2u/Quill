using System.Collections.Immutable;
using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;

namespace Quill.Editor.Projection;

/// <summary>
/// The active set (03-editor-state.md, 05-projection-and-view.md): the focused block, which holds the
/// activation head, and the inline nodes the head touches (ends included, every level of nesting).
/// </summary>
public sealed record ActiveSet(BlockId? FocusedBlock, ImmutableArray<InlineNode> ActiveNodes)
{
    public static ActiveSet None { get; } = new(null, []);

    /// <summary>Whether the node is active. Nodes are compared by identity within one inline tree.</summary>
    public bool IsActive(InlineNode node) => ActiveNodes.Contains(node);
}

/// <summary>Derives the active set from the activation head.</summary>
public static class Activation
{
    public static ActiveSet Compute(EditorState state)
    {
        // A block caret focuses its block: a TOC then shows its read-only source (Q59).
        if (state.Selection is { IsBlockCaret: true, Anchor: BoundaryPosition before } && ReferenceEquals(state.ActivationHead, state.Selection.Head)
            && DocumentTree.TryFind(state.Document, before.Parent) is ContainerBlock container && before.Index < container.Children.Count)
        {
            return new ActiveSet(container.Children[before.Index].Id, []);
        }

        if (state.ActivationHead is not TextPosition head || !DocumentTree.Contains(state.Document, head.Block))
        {
            return ActiveSet.None;
        }

        var block = DocumentTree.Find(state.Document, head.Block);
        if (block is not TextLeafBlock leaf)
        {
            return new ActiveSet(head.Block, []);
        }

        var tree = InlineTrees.Get(state.Document, leaf);
        var nodes = tree.NodesTouching(Math.Min(head.Offset, leaf.Text.Length)).Where(static node => CanActivate(node.Kind));
        return new ActiveSet(head.Block, [.. nodes]);
    }

    /// <summary>Kinds whose display changes when active: the ones with syntax to show.</summary>
    public static bool CanActivate(InlineKind kind) =>
        kind is not (InlineKind.Text or InlineKind.Url or InlineKind.Html or InlineKind.SoftBreak);

    /// <summary>A value key of the active nodes of a leaf, for memoization.</summary>
    internal static string? KeyOf(ImmutableArray<InlineNode> nodes)
    {
        if (nodes.IsEmpty)
        {
            return null;
        }

        var builder = new StringBuilder(";");
        foreach (var node in nodes)
        {
            builder.Append((int)node.Kind).Append('@').Append(node.Range.Start).Append('-').Append(node.Range.End).Append(';');
        }

        return builder.ToString();
    }

    /// <summary>The indices of the render model's owners named by an active key.</summary>
    internal static IReadOnlySet<int> OwnersOf(LeafRenderModel model, string? key)
    {
        if (key is null)
        {
            return EmptySet;
        }

        var set = new HashSet<int>();
        for (int i = 0; i < model.Owners.Length; i++)
        {
            var node = model.Owners[i];
            if (key.Contains($";{(int)node.Kind}@{node.Range.Start}-{node.Range.End};", StringComparison.Ordinal))
            {
                set.Add(i);
            }
        }

        return set;
    }

    private static readonly IReadOnlySet<int> EmptySet = new HashSet<int>();
}
