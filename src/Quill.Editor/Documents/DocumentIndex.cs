using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Quill.Editor.Documents;

/// <summary>
/// Maps block IDs to their parents, so a block can be found from its ID without walking the tree
/// (02-document-model.md). Each document version has its own index: structural edits derive a new one
/// from the old (cost proportional to the changed subtrees); text edits share it. Child positions are
/// remembered as hints and verified on use.
/// </summary>
internal sealed class DocumentIndex
{
    private static readonly ConditionalWeakTable<Document, DocumentIndex> Indexes = new();

    private readonly ImmutableDictionary<BlockId, BlockId> _parents;
    private readonly ConcurrentDictionary<BlockId, int> _hints;

    private DocumentIndex(BlockId root, ImmutableDictionary<BlockId, BlockId> parents, ConcurrentDictionary<BlockId, int> hints)
    {
        Root = root;
        _parents = parents;
        _hints = hints;
    }

    public BlockId Root { get; }

    /// <summary>The index of a document, built on first use.</summary>
    public static DocumentIndex Of(Document document) => Indexes.GetValue(document, Build);

    /// <summary>Associates an index derived from a previous version with a new document.</summary>
    public static void Attach(Document document, DocumentIndex index) => Indexes.AddOrUpdate(document, index);

    public bool Contains(BlockId id) => id == Root || _parents.ContainsKey(id);

    public BlockId? ParentOf(BlockId id) => _parents.TryGetValue(id, out var parent) ? parent : null;

    public bool IsAncestor(BlockId ancestor, BlockId id)
    {
        for (BlockId? current = ParentOf(id); current is { } parent; current = ParentOf(parent))
        {
            if (parent == ancestor)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The IDs from the root down to <paramref name="id"/>, inclusive.</summary>
    public List<BlockId> PathIds(BlockId id)
    {
        var path = new List<BlockId> { id };
        for (BlockId? current = ParentOf(id); current is { } parent; current = ParentOf(parent))
        {
            path.Add(parent);
        }

        path.Reverse();
        return path;
    }

    /// <summary>The position of a child in its parent, using the remembered hint when it is still right.</summary>
    public int IndexIn(ContainerBlock parent, BlockId child)
    {
        var children = parent.Children;
        if (_hints.TryGetValue(child, out int hint) && hint < children.Count && children[hint].Id == child)
        {
            return hint;
        }

        // Enumerated (indexing an ImmutableList costs O(log n) per child); every position seen is remembered.
        int index = 0;
        foreach (var candidate in children)
        {
            _hints[candidate.Id] = index;
            if (candidate.Id == child)
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    public void Hint(BlockId child, int index) => _hints[child] = index;

    /// <summary>An index for a version where <paramref name="added"/> subtrees were inserted under <paramref name="parent"/>.</summary>
    public DocumentIndex WithAdded(BlockId parent, IEnumerable<Block> added)
    {
        var builder = _parents.ToBuilder();
        foreach (var block in added)
        {
            Add(builder, parent, block);
        }

        return new DocumentIndex(Root, builder.ToImmutable(), _hints);
    }

    /// <summary>An index for a version where the <paramref name="removed"/> subtrees no longer exist.</summary>
    public DocumentIndex WithRemoved(IEnumerable<Block> removed)
    {
        var builder = _parents.ToBuilder();
        foreach (var block in removed)
        {
            Remove(builder, block);
        }

        return new DocumentIndex(Root, builder.ToImmutable(), _hints);
    }

    /// <summary>An index for a version where the subtree of <paramref name="old"/> became <paramref name="replacement"/> (same ID).</summary>
    public DocumentIndex WithReplaced(Block old, Block replacement)
    {
        if (old is not ContainerBlock && replacement is not ContainerBlock)
        {
            return this;
        }

        var builder = _parents.ToBuilder();
        if (old is ContainerBlock oldContainer)
        {
            foreach (var child in oldContainer.Children)
            {
                Remove(builder, child);
            }
        }

        if (replacement is ContainerBlock newContainer)
        {
            foreach (var child in newContainer.Children)
            {
                Add(builder, replacement.Id, child);
            }
        }

        return new DocumentIndex(Root, builder.ToImmutable(), _hints);
    }

    private static DocumentIndex Build(Document document)
    {
        var builder = ImmutableDictionary.CreateBuilder<BlockId, BlockId>();
        var hints = new ConcurrentDictionary<BlockId, int>();
        int position = 0;
        foreach (var child in document.Children)
        {
            hints[child.Id] = position++;
            Add(builder, document.Id, child, hints);
        }

        var index = new DocumentIndex(document.Id, builder.ToImmutable(), hints);
        return index;
    }

    private static void Add(ImmutableDictionary<BlockId, BlockId>.Builder builder, BlockId parent, Block block, ConcurrentDictionary<BlockId, int>? hints = null)
    {
        builder[block.Id] = parent;
        if (block is ContainerBlock container)
        {
            // The positions of the children, so that the first lookups do not search (a fresh index of a long document).
            int position = 0;
            foreach (var child in container.Children)
            {
                if (hints is not null)
                {
                    hints[child.Id] = position++;
                }

                Add(builder, block.Id, child, hints);
            }
        }
    }

    private static void Remove(ImmutableDictionary<BlockId, BlockId>.Builder builder, Block block)
    {
        builder.Remove(block.Id);
        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children)
            {
                Remove(builder, child);
            }
        }
    }
}
