using Quill.Editor.Documents;
using Quill.Editor.State;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>
/// One end of the selection as the view needs it: in a leaf, the display offset and affinity (the view
/// places the caret with the layout service, 05-projection-and-view.md); at a block boundary, the
/// virtual paragraph or the gap it denotes.
/// </summary>
public sealed record ViewPosition(BlockId Block, int DisplayOffset, Affinity Affinity, bool IsBoundary)
{
    public override string ToString() => IsBoundary ? $"{Block}[{DisplayOffset}]" : $"{Block}:{DisplayOffset}{(Affinity == Affinity.Backward ? "<" : string.Empty)}";
}

/// <summary>The selection declared on the document node; the caret overlay draws it after layout.</summary>
public sealed record SelectionInfo(ViewPosition Anchor, ViewPosition Head, bool HasFocus, bool IsBlockCaret = false) : IStructuralHash
{
    public bool IsCollapsed => Anchor == Head;

    public ulong StructuralHash =>
        NodeHash.Combine(NodeHash.Combine(NodeHash.Combine(NodeHash.Seed, ToString()), (HasFocus ? 1UL : 0UL) | (IsBlockCaret ? 2UL : 0UL)), (ulong)Head.Affinity);

    public override string ToString() => IsBlockCaret ? $"block {Anchor}" : IsCollapsed ? Head.ToString() : $"{Anchor}..{Head}";
}

/// <summary>
/// What the projection of one block depends on besides the block itself: focus, activation, selection
/// and composition (the memoization key of 05-projection-and-view.md). Offsets are in the leaf source.
/// </summary>
internal readonly record struct BlockViewState(
    bool IsFocused = false,
    bool IsActive = false,
    bool IsSelected = false,
    int SelectionStart = -1,
    int SelectionEnd = -1,
    string? ActiveKey = null,
    int CompositionStart = -1,
    int CompositionEnd = -1,
    int GapIndex = -1,
    bool HasBlockCaret = false)
{
    /// <summary>The state of a block nothing special happens to. Not <c>default</c>, which would zero the -1 markers.</summary>
    public static BlockViewState None { get; } = new(IsFocused: false);
}
