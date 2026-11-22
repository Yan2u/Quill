using System.Collections.Immutable;
using Quill.Editor.Inlines;

namespace Quill.Editor.Documents;

/// <summary>
/// Where a block is: the block and the containers above it with the child index at each level, from
/// the root down.
/// </summary>
internal readonly record struct BlockLocation(Block Block, ImmutableArray<(ContainerBlock Container, int Index)> Ancestors)
{
    public ContainerBlock? Parent => Ancestors.IsEmpty ? null : Ancestors[^1].Container;

    public int Index => Ancestors.IsEmpty ? -1 : Ancestors[^1].Index;

    /// <summary>The containers enclosing the block, innermost first.</summary>
    public IEnumerable<ContainerBlock> ContainersInnermostFirst()
    {
        for (int i = Ancestors.Length - 1; i >= 0; i--)
        {
            yield return Ancestors[i].Container;
        }
    }
}

/// <summary>
/// Finding and replacing blocks in the immutable tree. Edits copy the path from the root and share
/// everything else (02-document-model.md); copied and changed nodes get the new revision.
/// </summary>
internal static class DocumentTree
{
    public static BlockLocation Locate(Document document, BlockId id)
    {
        if (id == document.Id)
        {
            return new BlockLocation(document, []);
        }

        var index = DocumentIndex.Of(document);
        var path = index.PathIds(id);
        if (path[0] != document.Id)
        {
            throw new KeyNotFoundException($"Block {id} is not in the document.");
        }

        var ancestors = ImmutableArray.CreateBuilder<(ContainerBlock, int)>(path.Count - 1);
        ContainerBlock current = document;
        Block block = document;
        for (int i = 1; i < path.Count; i++)
        {
            int childIndex = index.IndexIn(current, path[i]);
            if (childIndex < 0)
            {
                throw new InvalidOperationException($"The index of {document.Id} is out of date for block {path[i]}.");
            }

            ancestors.Add((current, childIndex));
            block = current.Children[childIndex];
            if (i < path.Count - 1)
            {
                current = (ContainerBlock)block;
            }
        }

        return new BlockLocation(block, ancestors.MoveToImmutable());
    }

    public static bool Contains(Document document, BlockId id) => DocumentIndex.Of(document).Contains(id);

    public static Block Find(Document document, BlockId id) => Locate(document, id).Block;

    public static T Find<T>(Document document, BlockId id)
        where T : Block => (T)Locate(document, id).Block;

    public static Block? TryFind(Document document, BlockId id) => Contains(document, id) ? Locate(document, id).Block : null;

    /// <summary>
    /// Replaces the block that has <paramref name="replacement"/>'s ID. Blocks of the new subtree that
    /// are not the very same nodes as before get <paramref name="revision"/>.
    /// </summary>
    public static Document Replace(Document document, Block replacement, long revision)
    {
        var location = Locate(document, replacement.Id);
        var stamped = Stamp(replacement, location.Block, revision);
        var result = Rebuild(location.Ancestors, stamped, revision);
        DocumentIndex.Attach(result, DocumentIndex.Of(document).WithReplaced(location.Block, stamped));
        if (ReferenceIndex.TryGet(document, out var references))
        {
            ReferenceIndex.Attach(result, references!.WithReplaced(location.Block, stamped, location.Parent?.Id, document));
        }

        if (HeadingIndex.TryGet(document, out var headings))
        {
            HeadingIndex.Attach(result, headings!.WithReplaced(location.Block, stamped));
        }
        return result;
    }

    /// <summary>
    /// Inserts blocks as children of a container. The inserted subtrees get <paramref name="revision"/>:
    /// they may be rebuilt nodes that reuse IDs (list write-back, commits), whose old revisions must
    /// not identify their new content.
    /// </summary>
    public static Document Insert(Document document, BlockId parentId, int index, IReadOnlyList<Block> blocks, long revision)
    {
        var location = Locate(document, parentId);
        var parent = (ContainerBlock)location.Block;
        var stamped = blocks.Select(block => StampAll(block, revision)).ToList();
        blocks = stamped;
        var updated = parent with { Children = parent.Children.InsertRange(index, stamped), Revision = revision };
        var result = Rebuild(location.Ancestors, updated, revision);
        var resultIndex = DocumentIndex.Of(document).WithAdded(parentId, blocks);
        for (int i = 0; i < blocks.Count; i++)
        {
            resultIndex.Hint(blocks[i].Id, index + i);
        }

        DocumentIndex.Attach(result, resultIndex);
        if (ReferenceIndex.TryGet(document, out var references))
        {
            ReferenceIndex.Attach(result, references!.WithAdded(blocks));
        }

        if (HeadingIndex.TryGet(document, out var headings))
        {
            HeadingIndex.Attach(result, headings!.WithAdded(blocks));
        }
        return result;
    }

    public static Document Remove(Document document, BlockId parentId, int index, int count, out ImmutableArray<Block> removed, long revision)
    {
        var location = Locate(document, parentId);
        var parent = (ContainerBlock)location.Block;
        removed = [.. parent.Children.GetRange(index, count)];
        var updated = parent with { Children = parent.Children.RemoveRange(index, count), Revision = revision };
        var result = Rebuild(location.Ancestors, updated, revision);
        DocumentIndex.Attach(result, DocumentIndex.Of(document).WithRemoved(removed));
        if (ReferenceIndex.TryGet(document, out var references))
        {
            ReferenceIndex.Attach(result, references!.WithRemoved(removed));
        }

        if (HeadingIndex.TryGet(document, out var headings))
        {
            HeadingIndex.Attach(result, headings!.WithRemoved(removed));
        }
        return result;
    }

    /// <summary>Copies the containers above a changed node, from the bottom up.</summary>
    private static Document Rebuild(ImmutableArray<(ContainerBlock Container, int Index)> ancestors, Block changed, long revision)
    {
        Block current = changed;
        for (int i = ancestors.Length - 1; i >= 0; i--)
        {
            var (container, index) = ancestors[i];
            current = container with { Children = container.Children.SetItem(index, current), Revision = revision };
        }

        return (Document)current;
    }

    /// <summary>Gives the revision to every node of the subtree.</summary>
    private static Block StampAll(Block block, long revision)
    {
        if (block is ContainerBlock container)
        {
            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
            {
                children = children.SetItem(i, StampAll(children[i], revision));
            }

            return container with { Children = children, Revision = revision };
        }

        return block.Revision == revision ? block : block with { Revision = revision };
    }

    /// <summary>Gives the new revision to every node of the subtree that is not the same node as before.</summary>
    private static Block Stamp(Block replacement, Block old, long revision)
    {
        if (ReferenceEquals(replacement, old))
        {
            return old;
        }

        var previous = new Dictionary<BlockId, Block>();
        Collect(old, previous);
        return StampNode(replacement, previous, revision);

        static void Collect(Block block, Dictionary<BlockId, Block> map)
        {
            map[block.Id] = block;
            if (block is ContainerBlock container)
            {
                foreach (var child in container.Children)
                {
                    Collect(child, map);
                }
            }
        }
    }

    private static Block StampNode(Block block, Dictionary<BlockId, Block> previous, long revision)
    {
        if (previous.TryGetValue(block.Id, out var old) && ReferenceEquals(old, block))
        {
            return block;
        }

        if (block is ContainerBlock container)
        {
            var children = container.Children;
            for (int i = 0; i < children.Count; i++)
            {
                var stamped = StampNode(children[i], previous, revision);
                if (!ReferenceEquals(stamped, children[i]))
                {
                    children = children.SetItem(i, stamped);
                }
            }

            return container with { Children = children, Revision = revision };
        }

        return block with { Revision = revision };
    }
}
