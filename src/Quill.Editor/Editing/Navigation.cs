using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Where the caret can go in and around blocks: first and last caret positions, neighbouring leaves.
/// </summary>
internal static class Navigation
{
    /// <summary>The caret position at the start of a block: its first leaf, or the boundary before a block without text.</summary>
    public static Position First(Document document, Block block)
    {
        switch (block)
        {
            case LeafBlock leaf:
                return new TextPosition(leaf.Id, 0);
            case FootnoteDefinition or LinkReferenceDefinition:
                return new TextPosition(((ContainerBlock)block).Children[0].Id, 0);
            case ContainerBlock { Children.Count: > 0 } container and not Document:
                return First(document, container.Children[0]);
            case Document doc when doc.Children.Count > 0:
                return First(document, doc.Children[0]);
            default:
                var location = DocumentTree.Locate(document, block.Id);
                return new BoundaryPosition(location.Parent!.Id, location.Index);
        }
    }

    /// <summary>The caret position at the end of a block: its last leaf, or the boundary after a block without text.</summary>
    public static Position Last(Document document, Block block)
    {
        switch (block)
        {
            case LeafBlock leaf:
                return new TextPosition(leaf.Id, leaf.Text.Length);
            case FootnoteDefinition footnote:
                return new TextPosition(footnote.Content.Id, footnote.Content.Text.Length);
            case LinkReferenceDefinition definition:
                return new TextPosition(definition.Title.Id, definition.Title.Text.Length);
            case ContainerBlock { Children.Count: > 0 } container:
                return Last(document, container.Children[^1]);
            default:
                var location = DocumentTree.Locate(document, block.Id);
                return new BoundaryPosition(location.Parent!.Id, location.Index + 1);
        }
    }

    /// <summary>The last text leaf inside a block (the block itself when it is a leaf), or null.</summary>
    public static LeafBlock? LastLeaf(Block block)
    {
        return block switch
        {
            LeafBlock leaf => leaf,
            FootnoteDefinition footnote => footnote.Children.Count > 2 ? LastLeaf(footnote.Children[^1]) ?? footnote.Content : footnote.Content,
            LinkReferenceDefinition definition => definition.Title,
            Table table => table.Children.Count > 0 ? LastLeaf(table.Children[^1]) : null,
            ContainerBlock { Children.Count: > 0 } container => LastLeaf(container.Children[^1]),
            _ => null,
        };
    }

    /// <summary>The first text leaf inside a block (the block itself when it is a leaf), or null.</summary>
    public static LeafBlock? FirstLeaf(Block block)
    {
        return block switch
        {
            LeafBlock leaf => leaf,
            ContainerBlock { Children.Count: > 0 } container => FirstLeaf(container.Children[0]),
            _ => null,
        };
    }

    /// <summary>All leaves of the document in order (for whole-document operations and tests).</summary>
    public static IEnumerable<LeafBlock> Leaves(Block block)
    {
        if (block is LeafBlock leaf)
        {
            yield return leaf;
        }
        else if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                foreach (var inner in Leaves(child))
                {
                    yield return inner;
                }
            }
        }
    }

    /// <summary>The leaf before or after a leaf in document order, skipping blocks without text.</summary>
    public static LeafBlock? AdjacentLeaf(Document document, BlockId leafId, bool forward)
    {
        var location = DocumentTree.Locate(document, leafId);
        for (int level = location.Ancestors.Length - 1; level >= 0; level--)
        {
            var (container, index) = location.Ancestors[level];
            for (int sibling = forward ? index + 1 : index - 1; sibling >= 0 && sibling < container.Children.Count; sibling += forward ? 1 : -1)
            {
                var leaf = forward ? FirstLeaf(container.Children[sibling]) : LastLeaf(container.Children[sibling]);
                if (leaf is not null)
                {
                    return leaf;
                }
            }
        }

        return null;
    }
}
