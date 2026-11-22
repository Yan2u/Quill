using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;

namespace Quill.Editor.Projection;

/// <summary>How a piece of leaf source is displayed (05-projection-and-view.md).</summary>
public enum SegmentKind
{
    /// <summary>Text shown as written.</summary>
    Content,

    /// <summary>Syntax markers shown because their element is active.</summary>
    Marker,

    /// <summary>Syntax markers not shown: display length 0.</summary>
    Hidden,

    /// <summary>
    /// An object shown whole with display length 1 and no caret position inside. With an empty source
    /// range it is a display-only object, such as the image kept next to an active image's source.
    /// </summary>
    Atomic,

    /// <summary>A line break: display length 1.</summary>
    LineBreak,
}

/// <summary>A piece of a block's position map.</summary>
/// <param name="Source">The range in the leaf source.</param>
/// <param name="Display">The range in the displayed text.</param>
/// <param name="Node">The innermost inline node the piece belongs to; null for plain text and raw leaves.</param>
public readonly record struct Segment(TextRange Source, TextRange Display, SegmentKind Kind, InlineNode? Node = null);

/// <summary>
/// The map between source and display positions of one leaf (05-projection-and-view.md): segments in
/// order that tile the whole source.
/// </summary>
public sealed class BlockPositionMap : IEquatable<BlockPositionMap>
{
    public BlockPositionMap(BlockId block, ImmutableArray<Segment> segments)
    {
        Block = block;
        Segments = segments;
        SourceLength = segments.IsEmpty ? 0 : segments[^1].Source.End;
        DisplayLength = segments.IsEmpty ? 0 : segments[^1].Display.End;
    }

    public BlockId Block { get; }

    public ImmutableArray<Segment> Segments { get; }

    public int SourceLength { get; }

    public int DisplayLength { get; }

    /// <summary>
    /// The display position of a source offset. Hidden source maps to one display position; at a
    /// boundary between segments <see cref="Affinity.Backward"/> keeps to what comes before (before any
    /// display-only object) and <see cref="Affinity.Forward"/> to what comes after.
    /// </summary>
    public int ToDisplay(int offset, Affinity affinity)
    {
        offset = Math.Clamp(offset, 0, SourceLength);
        foreach (var segment in Segments)
        {
            if (segment.Source.ContainsStrictly(offset))
            {
                return segment.Kind switch
                {
                    SegmentKind.Content or SegmentKind.Marker => segment.Display.Start + offset - segment.Source.Start,
                    SegmentKind.Hidden => segment.Display.Start,
                    _ => affinity == Affinity.Backward ? segment.Display.Start : segment.Display.End,
                };
            }
        }

        if (affinity == Affinity.Backward)
        {
            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                var segment = Segments[i];
                if (segment.Source.End == offset && segment.Source.Length > 0)
                {
                    return segment.Display.End;
                }
            }

            return 0;
        }

        foreach (var segment in Segments)
        {
            if (segment.Source.Start == offset && segment.Source.Length > 0)
            {
                return segment.Display.Start;
            }
        }

        return DisplayLength;
    }

    /// <summary>
    /// The source offset of a display position. Where several source offsets share it (hidden markers),
    /// <see cref="Affinity.Backward"/> picks the one right after the text before and
    /// <see cref="Affinity.Forward"/> the one right before the text after: <c>**a|**</c> versus <c>**a**|</c>.
    /// </summary>
    public int ToSource(int display, Affinity affinity)
    {
        display = Math.Clamp(display, 0, DisplayLength);
        foreach (var segment in Segments)
        {
            if (segment.Display.ContainsStrictly(display))
            {
                return segment.Kind is SegmentKind.Content or SegmentKind.Marker
                    ? segment.Source.Start + display - segment.Display.Start
                    : affinity == Affinity.Backward ? segment.Source.Start : segment.Source.End;
            }
        }

        if (affinity == Affinity.Backward)
        {
            for (int i = Segments.Length - 1; i >= 0; i--)
            {
                var segment = Segments[i];
                if (segment.Display.End == display && segment.Display.Length > 0)
                {
                    return segment.Source.End;
                }
            }

            return 0;
        }

        foreach (var segment in Segments)
        {
            if (segment.Display.Start == display && segment.Display.Length > 0)
            {
                return segment.Source.Start;
            }
        }

        return SourceLength;
    }

    public bool Equals(BlockPositionMap? other) =>
        other is not null && other.Block == Block && other.Segments.SequenceEqual(Segments, SegmentComparer.Instance);

    public override bool Equals(object? obj) => Equals(obj as BlockPositionMap);

    public override int GetHashCode() => HashCode.Combine(Block, Segments.Length, DisplayLength);

    /// <summary>Compares segments by ranges, kind and node kind (nodes are not shared between parses).</summary>
    private sealed class SegmentComparer : IEqualityComparer<Segment>
    {
        public static SegmentComparer Instance { get; } = new();

        public bool Equals(Segment x, Segment y) =>
            x.Source == y.Source && x.Display == y.Display && x.Kind == y.Kind && x.Node?.Kind == y.Node?.Kind;

        public int GetHashCode(Segment obj) => HashCode.Combine(obj.Source, obj.Display, obj.Kind);
    }
}

/// <summary>
/// The position maps of the projected leaves, by block. A persistent map, so a projection after an edit
/// shares every untouched entry with the one before.
/// </summary>
public sealed class PositionMap
{
    public static PositionMap Empty { get; } = new(ImmutableDictionary<BlockId, BlockPositionMap>.Empty);

    internal PositionMap(ImmutableDictionary<BlockId, BlockPositionMap> blocks)
    {
        Blocks = blocks;
    }

    public ImmutableDictionary<BlockId, BlockPositionMap> Blocks { get; }

    public BlockPositionMap? this[BlockId block] => Blocks.GetValueOrDefault(block);

    /// <summary>The display offset of a text position, or null when its block has no map.</summary>
    public int? ToDisplay(TextPosition position) => this[position.Block]?.ToDisplay(position.Offset, position.Affinity);
}
