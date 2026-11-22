namespace Quill.Editor.Inlines;

/// <summary>
/// A half-open range [<see cref="Start"/>, <see cref="End"/>) of UTF-16 offsets in a leaf source or
/// in the displayed text of a block.
/// </summary>
public readonly record struct TextRange(int Start, int End)
{
    public int Length => End - Start;

    public bool IsEmpty => Start == End;

    /// <summary>Whether <paramref name="offset"/> lies in the range or touches either end.</summary>
    public bool ContainsInclusive(int offset) => offset >= Start && offset <= End;

    /// <summary>Whether <paramref name="offset"/> lies strictly between the ends.</summary>
    public bool ContainsStrictly(int offset) => offset > Start && offset < End;

    public bool Overlaps(TextRange other) => Start < other.End && other.Start < End;

    public override string ToString() => $"[{Start},{End})";
}
