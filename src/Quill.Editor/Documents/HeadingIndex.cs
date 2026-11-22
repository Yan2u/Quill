using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Quill.Editor.State;

namespace Quill.Editor.Documents;

/// <summary>
/// The headings of a document, for the table of contents (Q68). Like <see cref="DocumentIndex"/> and
/// the reference index, each document version has its own index, derived from the previous version's
/// by the edit (cost proportional to the changed blocks) or built on first use. <see cref="Version"/>
/// changes only when a heading changes, appears or disappears, so a TOC is projected again only then;
/// the document order is kept while the set of headings stays the same.
/// </summary>
public sealed class HeadingIndex
{
    private static readonly ConditionalWeakTable<Document, HeadingIndex> Indexes = new();
    private static long _versions;

    private readonly ImmutableDictionary<BlockId, Heading> _headings;
    private ImmutableArray<BlockId> _order;

    private HeadingIndex(ImmutableDictionary<BlockId, Heading> headings, ImmutableArray<BlockId> order, long version)
    {
        _headings = headings;
        _order = order;
        Version = version;
    }

    /// <summary>Changes whenever a heading changes, appears or disappears.</summary>
    public long Version { get; }

    public int Count => _headings.Count;

    public static HeadingIndex Of(Document document) => Indexes.GetValue(document, Build);

    /// <summary>The headings in document order.</summary>
    public IReadOnlyList<Heading> InOrder(Document document)
    {
        if (_order.IsDefault)
        {
            // Sorted only when headings appear or disappear: O(h log h) comparisons of paths.
            var ids = _headings.Keys.ToList();
            ids.Sort((a, b) => PositionOrder.Compare(document, new TextPosition(a, 0), new TextPosition(b, 0)));
            _order = [.. ids];
        }

        return _order.Select(id => _headings[id]).ToList();
    }

    internal static bool TryGet(Document document, out HeadingIndex? index) => Indexes.TryGetValue(document, out index);

    internal static void Attach(Document document, HeadingIndex index) => Indexes.AddOrUpdate(document, index);

    internal HeadingIndex WithReplaced(Block old, Block replacement)
    {
        var before = Collect(old);
        var after = Collect(replacement);
        if (before.Count == after.Count && before.All(heading => after.TryGetValue(heading.Key, out var now) && ReferenceEquals(now, heading.Value)))
        {
            return this;
        }

        var headings = _headings.RemoveRange(before.Keys).SetItems(after);
        bool sameSet = before.Count == after.Count && before.Keys.All(after.ContainsKey);
        return new HeadingIndex(headings, sameSet ? _order : default, Interlocked.Increment(ref _versions));
    }

    internal HeadingIndex WithAdded(IReadOnlyList<Block> blocks)
    {
        var added = blocks.SelectMany(block => Collect(block)).ToList();
        return added.Count == 0 ? this : new HeadingIndex(_headings.SetItems(added), default, Interlocked.Increment(ref _versions));
    }

    internal HeadingIndex WithRemoved(IReadOnlyList<Block> blocks)
    {
        var removed = blocks.SelectMany(block => Collect(block).Keys).ToList();
        return removed.Count == 0 ? this : new HeadingIndex(_headings.RemoveRange(removed), default, Interlocked.Increment(ref _versions));
    }

    private static HeadingIndex Build(Document document) =>
        new(Collect(document).ToImmutableDictionary(), default, Interlocked.Increment(ref _versions));

    private static Dictionary<BlockId, Heading> Collect(Block block)
    {
        var headings = new Dictionary<BlockId, Heading>();
        void Walk(Block current)
        {
            switch (current)
            {
                case Heading heading:
                    headings[heading.Id] = heading;
                    break;
                case ContainerBlock container:
                    foreach (var child in container.Children)
                    {
                        Walk(child);
                    }

                    break;
            }
        }

        Walk(block);
        return headings;
    }
}
