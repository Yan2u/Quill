using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Quill.Editor.Documents;
using Quill.Editor.State;

namespace Quill.Editor.Inlines;

/// <summary>
/// The document-level definition and reference index (06-markdown-io.md): link reference definitions
/// and footnote definitions by normalized label, and how often each footnote is referenced. Like
/// <see cref="DocumentIndex"/>, each document version has its own index, derived from the previous
/// version's by the edit (cost proportional to the changed blocks), or built on first use.
/// </summary>
/// <remarks>
/// Definitions may come before or after their references; with duplicate labels the first definition
/// in the document wins, without a warning (Typora). Footnote definitions are only recognized at the
/// top level of the document, but the index takes them wherever they are, as they appear while editing.
/// </remarks>
public sealed class ReferenceIndex
{
    private static readonly ConditionalWeakTable<Document, ReferenceIndex> Indexes = new();

    private readonly ImmutableDictionary<string, ImmutableHashSet<BlockId>> _links;
    private readonly ImmutableDictionary<string, ImmutableHashSet<BlockId>> _footnotes;
    private readonly ImmutableDictionary<BlockId, string> _labels;
    private readonly ImmutableDictionary<string, int> _footnoteReferences;

    private ReferenceIndex(
        ImmutableDictionary<string, ImmutableHashSet<BlockId>> links,
        ImmutableDictionary<string, ImmutableHashSet<BlockId>> footnotes,
        ImmutableDictionary<BlockId, string> labels,
        ImmutableDictionary<string, int> footnoteReferences,
        long linkLabelsVersion,
        long footnotesVersion)
    {
        _links = links;
        _footnotes = footnotes;
        _labels = labels;
        _footnoteReferences = footnoteReferences;
        LinkLabelsVersion = linkLabelsVersion;
        FootnotesVersion = footnotesVersion;
    }

    /// <summary>Changes whenever the set of defined link labels changes; inline parses depend only on that set.</summary>
    public long LinkLabelsVersion { get; }

    /// <summary>Changes whenever footnote definition labels or reference counts change.</summary>
    public long FootnotesVersion { get; }

    public static ReferenceIndex Of(Document document) => Indexes.GetValue(document, Build);

    public bool IsLinkDefined(string normalizedLabel) => _links.ContainsKey(normalizedLabel);

    /// <summary>The definition a link label resolves to: the first one in the document.</summary>
    public BlockId? ResolveLink(Document document, string normalizedLabel) => First(document, _links, normalizedLabel);

    /// <summary>The definition a footnote label resolves to: the first one in the document.</summary>
    public BlockId? ResolveFootnote(Document document, string normalizedLabel) => First(document, _footnotes, normalizedLabel);

    /// <summary>How many footnote references with this label the text leaves of the document hold.</summary>
    public int FootnoteReferenceCount(string normalizedLabel) => _footnoteReferences.GetValueOrDefault(normalizedLabel);

    /// <summary>The normalized label of a definition block.</summary>
    public string? LabelOf(BlockId definition) => _labels.GetValueOrDefault(definition);

    internal static bool TryGet(Document document, out ReferenceIndex? index) => Indexes.TryGetValue(document, out index);

    internal static void Attach(Document document, ReferenceIndex index) => Indexes.AddOrUpdate(document, index);

    /// <summary>A fresh index of the document, ignoring any derived one (for tests of the incremental updates).</summary>
    internal static ReferenceIndex BuildFresh(Document document) => Build(document);

    /// <summary>The link labels defined in one index and not in the other.</summary>
    internal IEnumerable<string> ChangedLinkLabels(ReferenceIndex previous)
    {
        foreach (string label in _links.Keys)
        {
            if (!previous._links.ContainsKey(label))
            {
                yield return label;
            }
        }

        foreach (string label in previous._links.Keys)
        {
            if (!_links.ContainsKey(label))
            {
                yield return label;
            }
        }
    }

    /// <summary>All footnote definitions.</summary>
    internal IEnumerable<BlockId> FootnoteDefinitionIds => _footnotes.Values.SelectMany(static set => set);

    /// <summary>Whether both indexes hold the same entries (versions aside).</summary>
    internal bool HasSameEntries(ReferenceIndex other) =>
        SameSets(_links, other._links)
        && SameSets(_footnotes, other._footnotes)
        && _labels.Count == other._labels.Count && _labels.All(entry => other._labels.TryGetValue(entry.Key, out var label) && label == entry.Value)
        && _footnoteReferences.Count == other._footnoteReferences.Count && _footnoteReferences.All(entry => other._footnoteReferences.GetValueOrDefault(entry.Key) == entry.Value);

    private static bool SameSets(ImmutableDictionary<string, ImmutableHashSet<BlockId>> left, ImmutableDictionary<string, ImmutableHashSet<BlockId>> right) =>
        left.Count == right.Count && left.All(entry => right.TryGetValue(entry.Key, out var set) && set.SetEquals(entry.Value));

    /// <summary>The index after the subtree <paramref name="old"/> became <paramref name="replacement"/> (same ID).</summary>
    internal ReferenceIndex WithReplaced(Block old, Block replacement, BlockId? parentOfOld, Document before)
    {
        if (old is DefinitionSlot oldSlot && replacement is DefinitionSlot newSlot)
        {
            if (oldSlot.Text == newSlot.Text || oldSlot.SlotKind is not (DefinitionSlotKind.LinkLabel or DefinitionSlotKind.FootnoteName) || parentOfOld is not { } definition)
            {
                return this;
            }

            var editor = new Editor(this);
            var parent = DocumentTree.Find(before, definition);
            editor.RemoveDefinition(parent);
            editor.AddDefinition(parent is LinkReferenceDefinition link
                ? link with { Children = link.Children.SetItem(0, newSlot) }
                : ((FootnoteDefinition)parent) with { Children = ((FootnoteDefinition)parent).Children.SetItem(0, newSlot) });
            return editor.Build();
        }

        if (old is LeafBlock oldLeaf && replacement is LeafBlock newLeaf)
        {
            if (!CountsReferences(oldLeaf) && !CountsReferences(newLeaf))
            {
                return this;
            }

            var oldLabels = CountsReferences(oldLeaf) ? FootnoteReferenceScanner.Labels(oldLeaf.Text) : null;
            var newLabels = CountsReferences(newLeaf) ? FootnoteReferenceScanner.Labels(newLeaf.Text) : null;
            if (oldLabels is null && newLabels is null)
            {
                return this;
            }

            var editor = new Editor(this);
            editor.CountReferences(oldLabels, -1);
            editor.CountReferences(newLabels, +1);
            return editor.Build();
        }

        var subtree = new Editor(this);
        subtree.Remove(old);
        subtree.Add(replacement);
        return subtree.Build();
    }

    internal ReferenceIndex WithAdded(IEnumerable<Block> added)
    {
        var editor = new Editor(this);
        foreach (var block in added)
        {
            editor.Add(block);
        }

        return editor.Build();
    }

    internal ReferenceIndex WithRemoved(IEnumerable<Block> removed)
    {
        var editor = new Editor(this);
        foreach (var block in removed)
        {
            editor.Remove(block);
        }

        return editor.Build();
    }

    /// <summary>How many indexes were built from scratch, for tests (a keystroke must derive, not rebuild).</summary>
    internal static long FullBuilds => Interlocked.Read(ref _fullBuilds);

    private static long _fullBuilds;

    private static ReferenceIndex Build(Document document)
    {
        var empty = new ReferenceIndex(
            ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Empty,
            ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Empty,
            ImmutableDictionary<BlockId, string>.Empty,
            ImmutableDictionary<string, int>.Empty,
            0,
            0);
        Interlocked.Increment(ref _fullBuilds);
        var editor = new Editor(empty);
        foreach (var child in document.Children)
        {
            editor.Add(child);
        }

        return editor.Build();
    }

    private static bool CountsReferences(LeafBlock leaf) => leaf is TextLeafBlock { InlineSyntax: InlineSyntaxSet.Full };

    private static BlockId? First(Document document, ImmutableDictionary<string, ImmutableHashSet<BlockId>> map, string label)
    {
        if (!map.TryGetValue(label, out var definitions) || definitions.IsEmpty)
        {
            return null;
        }

        if (definitions.Count == 1)
        {
            return definitions.First();
        }

        BlockId? first = null;
        foreach (var id in definitions)
        {
            if (first is not { } current
                || PositionOrder.Compare(document, new BoundaryPosition(id, 0), new BoundaryPosition(current, 0)) < 0)
            {
                first = id;
            }
        }

        return first;
    }

    /// <summary>Accumulates changes and bumps the versions that the changes affect.</summary>
    private sealed class Editor
    {
        private readonly ReferenceIndex _source;
        private readonly ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Builder _links;
        private readonly ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Builder _footnotes;
        private readonly ImmutableDictionary<BlockId, string>.Builder _labels;
        private readonly ImmutableDictionary<string, int>.Builder _references;
        private bool _linkLabelsChanged;
        private bool _footnotesChanged;
        private bool _changed;

        public Editor(ReferenceIndex source)
        {
            _source = source;
            _links = source._links.ToBuilder();
            _footnotes = source._footnotes.ToBuilder();
            _labels = source._labels.ToBuilder();
            _references = source._footnoteReferences.ToBuilder();
        }

        public void Add(Block block) => Walk(block, adding: true);

        public void Remove(Block block) => Walk(block, adding: false);

        public void AddDefinition(Block definition)
        {
            switch (definition)
            {
                case LinkReferenceDefinition link:
                    string label = LinkLabel.Normalize(link.Label.Text);
                    _labels[link.Id] = label;
                    _linkLabelsChanged |= AddTo(_links, label, link.Id);
                    _changed = true;
                    break;
                case FootnoteDefinition footnote:
                    string name = LinkLabel.Normalize(footnote.Name.Text);
                    _labels[footnote.Id] = name;
                    AddTo(_footnotes, name, footnote.Id);
                    _footnotesChanged = true;
                    _changed = true;
                    break;
            }
        }

        public void RemoveDefinition(Block definition)
        {
            if (!_labels.TryGetValue(definition.Id, out string? label))
            {
                return;
            }

            _labels.Remove(definition.Id);
            if (definition is LinkReferenceDefinition)
            {
                _linkLabelsChanged |= RemoveFrom(_links, label, definition.Id);
                _changed = true;
            }
            else
            {
                RemoveFrom(_footnotes, label, definition.Id);
                _footnotesChanged = true;
                _changed = true;
            }
        }

        public void CountReferences(List<string>? labels, int delta)
        {
            if (labels is null)
            {
                return;
            }

            foreach (string label in labels)
            {
                int count = _references.GetValueOrDefault(label) + delta;
                if (count == 0)
                {
                    _references.Remove(label);
                }
                else
                {
                    _references[label] = count;
                }

                _footnotesChanged = true;
                _changed = true;
            }
        }

        public ReferenceIndex Build()
        {
            if (!_changed)
            {
                return _source;
            }

            return new ReferenceIndex(
                _links.ToImmutable(),
                _footnotes.ToImmutable(),
                _labels.ToImmutable(),
                _references.ToImmutable(),
                _source.LinkLabelsVersion + (_linkLabelsChanged ? 1 : 0),
                _source.FootnotesVersion + (_footnotesChanged ? 1 : 0));
        }

        private void Walk(Block block, bool adding)
        {
            switch (block)
            {
                case LinkReferenceDefinition or FootnoteDefinition:
                    if (adding)
                    {
                        AddDefinition(block);
                    }
                    else
                    {
                        RemoveDefinition(block);
                    }

                    break;
                case LeafBlock leaf when CountsReferences(leaf):
                    CountReferences(FootnoteReferenceScanner.Labels(leaf.Text), adding ? 1 : -1);
                    break;
            }

            if (block is ContainerBlock container)
            {
                foreach (var child in container.Children)
                {
                    Walk(child, adding);
                }
            }
        }

        /// <summary>Adds a definition; true when the label had none before.</summary>
        private static bool AddTo(ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Builder map, string label, BlockId id)
        {
            bool isNew = !map.TryGetValue(label, out var set) || set.IsEmpty;
            map[label] = (set ?? []).Add(id);
            return isNew;
        }

        /// <summary>Removes a definition; true when the label has none left.</summary>
        private static bool RemoveFrom(ImmutableDictionary<string, ImmutableHashSet<BlockId>>.Builder map, string label, BlockId id)
        {
            if (!map.TryGetValue(label, out var set))
            {
                return false;
            }

            set = set.Remove(id);
            if (set.IsEmpty)
            {
                map.Remove(label);
                return true;
            }

            map[label] = set;
            return false;
        }
    }
}
