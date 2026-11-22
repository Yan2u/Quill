using Quill.Editor.Documents;

namespace Quill.Editor.State;

/// <summary>
/// Which side a position clings to when two positions look the same: the hidden markers of
/// <c>**a|**</c> versus <c>**a**|</c>, or the end of one visual line versus the start of the next
/// (03-editor-state.md).
/// </summary>
public enum Affinity
{
    Backward,
    Forward,
}

/// <summary>
/// A place in the document: inside a leaf's source (<see cref="TextPosition"/>) or between two children
/// of a container (<see cref="BoundaryPosition"/>).
/// </summary>
public abstract record Position
{
    /// <summary>Whether both positions denote the same place, ignoring affinity.</summary>
    public abstract bool SamePlace(Position other);
}

/// <summary>
/// A position in the source of a leaf block. <see cref="Offset"/> counts UTF-16 code units and lies on
/// a grapheme cluster boundary.
/// </summary>
public sealed record TextPosition(BlockId Block, int Offset, Affinity Affinity = Affinity.Forward) : Position
{
    public override bool SamePlace(Position other) =>
        other is TextPosition text && text.Block == Block && text.Offset == Offset;

    public override string ToString() => $"{Block}:{Offset}{(Affinity == Affinity.Backward ? "<" : string.Empty)}";
}

/// <summary>
/// The boundary before child <see cref="Index"/> of a container (<see cref="Index"/> equal to the
/// child count is the end). Holds the caret in a virtual paragraph and the ends of a selection that
/// covers whole blocks.
/// </summary>
public sealed record BoundaryPosition(BlockId Parent, int Index) : Position
{
    public override bool SamePlace(Position other) => other == this;

    public override string ToString() => $"{Parent}[{Index}]";
}

/// <summary>
/// A selection from <see cref="Anchor"/> (where it started) to <see cref="Head"/> (the moving end). A
/// caret is a collapsed selection.
/// </summary>
public sealed record TextSelection(Position Anchor, Position Head)
{
    public static TextSelection Caret(Position position) => new(position, position);

    public static TextSelection Caret(BlockId block, int offset, Affinity affinity = Affinity.Forward) =>
        Caret(new TextPosition(block, offset, affinity));

    /// <summary>
    /// The caret standing on a block that has no text positions (a TOC or a rule, Q59, Q60): it covers
    /// the block from the boundary before it to the boundary after it, like a selection of the block,
    /// but only Backspace and Delete edit (they delete the block) and the view draws it differently.
    /// </summary>
    public static TextSelection BlockCaret(BlockId parent, int index) =>
        new(new BoundaryPosition(parent, index), new BoundaryPosition(parent, index + 1)) { IsBlockCaret = true };

    /// <summary>Whether this is a <see cref="BlockCaret"/> rather than a selection of the same range.</summary>
    public bool IsBlockCaret { get; init; }

    public bool IsCollapsed => Anchor.SamePlace(Head);

    public override string ToString() => IsBlockCaret ? $"block {Anchor}" : IsCollapsed ? Head.ToString() : $"{Anchor}..{Head}";
}
