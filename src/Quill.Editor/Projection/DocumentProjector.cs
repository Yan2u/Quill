using System.Collections.Immutable;
using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>Work done by one projection.</summary>
/// <param name="IsFull">Whether every block was projected (the first projection).</param>
/// <param name="BlocksProjected">Blocks whose node was built rather than reused.</param>
/// <param name="LeavesRendered">Leaves rendered (phase two) in this projection.</param>
public sealed record ProjectionStatistics(bool IsFull, int BlocksProjected, int LeavesRendered);

/// <summary>The output of <c>Projection(s, d)</c> (05-projection-and-view.md).</summary>
public sealed record ProjectionResult(VNode Root, PositionMap Map, ActiveSet Active, SelectionInfo Selection, ProjectionStatistics Statistics);

/// <summary>
/// Projects editor states to VNode trees and position maps. Projection is a pure function of the
/// state: this class only memoizes it. A block's node is reused, as the same instance, while the block
/// node, its context (list level, numbering, table cell alignment) and its view state are unchanged,
/// so the reconciler skips it by reference; a container is entered only when it changed or holds a
/// block whose view state or references changed. Untouched leaves keep their inline trees and render
/// models (two-phase projection). Called on the UI thread.
/// </summary>
public sealed partial class DocumentProjector
{
    private readonly Dictionary<BlockId, Entry> _entries = [];
    private readonly Dictionary<string, HashSet<BlockId>> _leavesByLabel = new(StringComparer.Ordinal);
    private Dictionary<BlockId, BlockViewState> _states = [];
    private ReferenceIndex? _references;
    private readonly HashSet<BlockId> _tocs = [];
    private long _headingsVersion = -1;
    private ImmutableDictionary<BlockId, BlockPositionMap> _maps = ImmutableDictionary<BlockId, BlockPositionMap>.Empty;
    private VNode? _root;
    private VNode[] _rootChildren = [];
    private SelectionInfo? _rootSelection;
    private HoverInfo? _rootHover;
    private VNode? _gap;
    private VNode? _stack;
    private Document? _lastDocument;
    // The top-level blocks the stack's nodes were projected from, updated in place (no copy per key).
    private readonly List<Block> _topBlocks = [];

    // State of the projection in progress.
    private Document _document = null!;
    private ReferenceIndex _currentReferences = null!;
    private Dictionary<BlockId, BlockViewState> _current = [];
    private HashSet<BlockId> _mustVisit = [];
    private int _blocksProjected;
    private int _leavesRendered;

    public ProjectionResult Project(EditorState state)
    {
        _document = state.Document;
        _currentReferences = ReferenceIndex.Of(_document);
        var active = Activation.Compute(state);
        _current = ViewStates.Compute(state, active);
        _mustVisit = [];
        _blocksProjected = 0;
        _leavesRendered = 0;
        bool full = _entries.Count == 0;
        if (full)
        {
            OperationCounters.Record(CountedOperation.FullProjection);
        }
        else
        {
            MarkChangedStates();
            MarkChangedReferences();
            MarkChangedHeadings();
        }

        // A selection or activation change inside the same document version reaches no block unless
        // marked: then the top-level nodes are the previous ones.
        VNode[] children;
        if (!full && _mustVisit.Count == 0 && ReferenceEquals(_document, _lastDocument))
        {
            children = _rootChildren;
        }
        else if (!full && TryProjectTopLevelInPlace(out var inPlace))
        {
            children = inPlace;
        }
        else
        {
            children = ProjectChildren(_document, static (_, _) => default, [.. _topBlocks.Select(static block => block.Id)], out _);
            _topBlocks.Clear();
            _topBlocks.AddRange(_document.Children);
        }

        var selection = DescribeSelection(state);
        if (_stack is null || (!ReferenceEquals(children, _rootChildren) && !SameNodes(children, _rootChildren)))
        {
            // The stack owns the array: it is never written again (a change copies it first).
            _stack = V.ElementOwningChildren(ProjectionNames.VirtualStack, children);
            _rootChildren = children;
        }

        // The document node only holds the selection, the hover flyout and the stack: a caret move or
        // a hover rebuilds just it.
        var hover = DescribeHover(state);
        if (_root is null || selection != _rootSelection || hover != _rootHover || !ReferenceEquals(_root.Children[0], _stack))
        {
            var builder = V.Element(ProjectionNames.Document).Set(ProjectionNames.Selection, selection);
            if (_document.Children.Count > 0 && _document.Children[0] is SourceLine)
            {
                builder.Set(ProjectionNames.IsSource, true);
            }

            if (hover is not null)
            {
                builder.Set(ProjectionNames.Hover, hover);
            }

            _root = builder.Add(_stack).Build();
            _rootSelection = selection;
            _rootHover = hover;
        }

        var root = _root;
        _states = _current;
        _lastDocument = _document;
        _references = _currentReferences;
        return new ProjectionResult(root, new PositionMap(_maps), active, _rootSelection!, new ProjectionStatistics(full, _blocksProjected, _leavesRendered));
    }

    /// <summary>
    /// The usual edit replaces, inserts or removes a few top-level blocks. The unchanged blocks at
    /// both ends (by reference) keep their nodes; only the blocks between them go through
    /// <see cref="ProjectBlock"/> (top-level blocks all have the default context), and the stack's node
    /// array is copied once. Blocks at the ends whose view state or references changed are projected
    /// again. Not used while a virtual paragraph sits between top-level blocks.
    /// </summary>
    private bool TryProjectTopLevelInPlace(out VNode[] children)
    {
        children = [];
        if (_lastDocument is null
            || _rootChildren.Length != _topBlocks.Count
            || _current.GetValueOrDefault(_document.Id, BlockViewState.None).GapIndex >= 0)
        {
            return false;
        }

        var blocks = _document.Children;
        int oldCount = _topBlocks.Count;
        int newCount = blocks.Count;
        int limit = Math.Min(oldCount, newCount);
        int prefix = 0;
        foreach (var block in blocks)
        {
            if (prefix == limit || !ReferenceEquals(block, _topBlocks[prefix]))
            {
                break;
            }

            prefix++;
        }

        // From the end by index: ImmutableList.Reverse() would copy the list.
        int suffix = 0;
        while (prefix + suffix < limit && ReferenceEquals(blocks[newCount - 1 - suffix], _topBlocks[oldCount - 1 - suffix]))
        {
            suffix++;
        }

        bool unchanged = prefix == oldCount && oldCount == newCount;
        var nodes = unchanged ? _rootChildren : new VNode[newCount];
        if (!unchanged)
        {
            Array.Copy(_rootChildren, 0, nodes, 0, prefix);
            Array.Copy(_rootChildren, oldCount - suffix, nodes, newCount - suffix, suffix);

            int index = prefix;
            var middle = new List<Block>(newCount - prefix - suffix);
            foreach (var block in blocks.GetRange(prefix, newCount - prefix - suffix))
            {
                nodes[index++] = ProjectBlock(block, default);
                middle.Add(block);
            }

            for (int i = prefix; i < oldCount - suffix; i++)
            {
                if (!DocumentTree.Contains(_document, _topBlocks[i].Id))
                {
                    Forget(_topBlocks[i].Id);
                }
            }

            _topBlocks.RemoveRange(prefix, oldCount - prefix - suffix);
            _topBlocks.InsertRange(prefix, middle);
        }

        // Unchanged blocks whose view state or references changed.
        if (_mustVisit.Count > 0)
        {
            for (int i = 0; i < newCount; i++)
            {
                if ((i < prefix || i >= newCount - suffix) && _mustVisit.Contains(_topBlocks[i].Id))
                {
                    if (ReferenceEquals(nodes, _rootChildren))
                    {
                        nodes = (VNode[])nodes.Clone();
                    }

                    nodes[i] = ProjectBlock(_topBlocks[i], default);
                }
            }
        }

        children = nodes;
        return true;
    }

    /// <summary>Blocks whose view state changed since the last projection, and their ancestors, must be entered.</summary>
    private void MarkChangedStates()
    {
        foreach (var (id, state) in _current)
        {
            if (!_states.TryGetValue(id, out var previous) || previous != state)
            {
                MarkPath(id);
            }
        }

        foreach (var id in _states.Keys)
        {
            if (!_current.ContainsKey(id))
            {
                MarkPath(id);
            }
        }
    }

    /// <summary>
    /// Leaves whose parse may change because a link label became defined or undefined, and footnote
    /// definitions whose back links may have changed, must be entered (06-markdown-io.md).
    /// </summary>
    private void MarkChangedReferences()
    {
        if (_references is null || ReferenceEquals(_references, _currentReferences))
        {
            return;
        }

        foreach (string label in _currentReferences.ChangedLinkLabels(_references))
        {
            if (_leavesByLabel.TryGetValue(label, out var leaves))
            {
                foreach (var leaf in leaves)
                {
                    MarkPath(leaf);
                }
            }
        }

        foreach (var definition in _currentReferences.FootnoteDefinitionIds)
        {
            MarkPath(definition);
        }
    }

    /// <summary>TOC blocks show every heading: they must be entered when a heading changed (Q68).</summary>
    private void MarkChangedHeadings()
    {
        if (_tocs.Count == 0)
        {
            return;
        }

        long version = HeadingIndex.Of(_document).Version;
        if (version != _headingsVersion)
        {
            foreach (var toc in _tocs)
            {
                MarkPath(toc);
            }
        }
    }

    private void MarkPath(BlockId id)
    {
        if (!DocumentTree.Contains(_document, id))
        {
            return;
        }

        foreach (var ancestor in DocumentIndex.Of(_document).PathIds(id))
        {
            _mustVisit.Add(ancestor);
        }
    }

    private VNode[] ProjectChildren(ContainerBlock container, Func<ContainerBlock, int, BlockContext> context, ImmutableArray<BlockId> previousIds, out ImmutableArray<BlockId> childIds)
    {
        int gap = _current.GetValueOrDefault(container.Id, BlockViewState.None).GapIndex;
        var nodes = new VNode[container.Children.Count + (gap >= 0 ? 1 : 0)];
        var ids = ImmutableArray.CreateBuilder<BlockId>(container.Children.Count);
        int n = 0;
        int index = 0;
        foreach (var child in container.Children)
        {
            // Enumerated, not indexed: indexing an ImmutableList costs O(log n) per child.
            if (index == gap)
            {
                nodes[n++] = VirtualParagraph(container, index);
            }

            ids.Add(child.Id);
            nodes[n++] = ProjectBlock(child, context(container, index));
            index++;
        }

        if (gap >= container.Children.Count)
        {
            nodes[n++] = VirtualParagraph(container, container.Children.Count);
        }

        childIds = ids.MoveToImmutable();
        ForgetVanished(previousIds, childIds);
        return nodes;
    }

    private VNode ProjectBlock(Block block, BlockContext context)
    {
        var state = _current.GetValueOrDefault(block.Id, BlockViewState.None);
        if (_entries.TryGetValue(block.Id, out var entry)
            && ReferenceEquals(entry.Block, block)
            && entry.Context == context
            && entry.State == state
            && !_mustVisit.Contains(block.Id))
        {
            return entry.Node;
        }

        _blocksProjected++;
        var previousChildren = entry?.ChildIds ?? [];
        var previousCandidates = entry?.Candidates ?? [];
        var created = new Entry(block, context, state);
        created.Node = Build(block, context, state, previousChildren, created, previousCandidates);

        // A paragraph committed into other blocks keeps its ID (02-document-model.md): drop what the
        // previous kind of block left behind.
        if (block is not TextLeafBlock && !previousCandidates.IsEmpty)
        {
            SetCandidates(block.Id, previousCandidates, []);
        }

        if (block is not LeafBlock && entry?.Block is LeafBlock)
        {
            _maps = _maps.Remove(block.Id);
        }

        if (block is not ContainerBlock)
        {
            ForgetVanished(previousChildren, []);
        }
        _entries[block.Id] = created;
        return created.Node;
    }

    /// <summary>Drops the memoized data of children that left the document.</summary>
    private void ForgetVanished(ImmutableArray<BlockId> previous, ImmutableArray<BlockId> current)
    {
        if (previous.IsDefaultOrEmpty || previous.AsSpan().SequenceEqual(current.AsSpan()))
        {
            return;
        }

        HashSet<BlockId>? keep = null;
        foreach (var id in previous)
        {
            keep ??= [.. current];
            if (!keep.Contains(id) && !DocumentTree.Contains(_document, id))
            {
                Forget(id);
            }
        }
    }

    private void Forget(BlockId id)
    {
        if (!_entries.Remove(id, out var entry))
        {
            return;
        }

        _maps = _maps.Remove(id);
        SetCandidates(id, entry.Candidates, []);
        foreach (var child in entry.ChildIds)
        {
            if (!DocumentTree.Contains(_document, child))
            {
                Forget(child);
            }
        }
    }

    private void SetCandidates(BlockId leaf, ImmutableArray<string> previous, ImmutableArray<string> current)
    {
        foreach (string label in previous)
        {
            if (_leavesByLabel.TryGetValue(label, out var leaves))
            {
                leaves.Remove(leaf);
                if (leaves.Count == 0)
                {
                    _leavesByLabel.Remove(label);
                }
            }
        }

        foreach (string label in current)
        {
            if (!_leavesByLabel.TryGetValue(label, out var leaves))
            {
                _leavesByLabel[label] = leaves = [];
            }

            leaves.Add(leaf);
        }
    }

    /// <summary>The flyout of the element under the pointer, found again in the current document (10, Q29).</summary>
    private HoverInfo? DescribeHover(EditorState state)
    {
        if (!Input.HoverTargets.TryParse(state.Interaction.HoverTarget, out var block, out var range)
            || DocumentTree.TryFind(_document, block) is not TextLeafBlock leaf
            || _maps.GetValueOrDefault(block) is not { } map)
        {
            return null;
        }

        var node = InlineTrees.Get(_currentReferences, leaf).Descendants().FirstOrDefault(node => node.Range == range);
        if (node is null || Input.HoverTargets.Shows(node, _document) is not { } shown)
        {
            return null;
        }

        var hover = new HoverInfo(KeyOf(block), map.ToDisplay(range.Start, State.Affinity.Forward), map.ToDisplay(range.End, State.Affinity.Backward), shown.Text, shown.IsWarning);
        return hover == _rootHover ? _rootHover : hover;
    }

    private SelectionInfo DescribeSelection(EditorState state)
    {
        var selection = new SelectionInfo(Describe(state.Selection.Anchor), Describe(state.Selection.Head), state.Interaction.HasFocus, state.Selection.IsBlockCaret);
        return selection == _rootSelection ? _rootSelection! : selection;
    }

    private ViewPosition Describe(Position position) => position switch
    {
        TextPosition text => new ViewPosition(text.Block, _maps.GetValueOrDefault(text.Block)?.ToDisplay(text.Offset, text.Affinity) ?? text.Offset, text.Affinity, false),
        BoundaryPosition boundary => new ViewPosition(boundary.Parent, boundary.Index, Affinity.Forward, true),
        _ => throw new ArgumentOutOfRangeException(nameof(position)),
    };

    private static bool SameNodes(VNode[] left, VNode[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (int i = 0; i < left.Length; i++)
        {
            if (!ReferenceEquals(left[i], right[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The memoized projection of one block.</summary>
    private sealed class Entry(Block block, BlockContext context, BlockViewState state)
    {
        public Block Block { get; } = block;

        public BlockContext Context { get; } = context;

        public BlockViewState State { get; } = state;

        public VNode Node { get; set; } = null!;

        public ImmutableArray<BlockId> ChildIds { get; set; } = [];

        public ImmutableArray<string> Candidates { get; set; } = [];
    }
}

/// <summary>What a block's projection takes from its ancestors.</summary>
/// <param name="ListLevel">The number of lists around the block.</param>
/// <param name="Number">The displayed number of an ordered list item; 0 otherwise.</param>
/// <param name="IsHeader">Whether a table row is the header row.</param>
/// <param name="Alignments">The column alignments, for table rows (compared by instance: tables keep theirs until changed).</param>
internal readonly record struct BlockContext(int ListLevel = 0, int Number = 0, TableAlignment Alignment = TableAlignment.None, bool IsHeader = false, ImmutableArray<TableAlignment> Alignments = default, string? CodeInfo = null);
