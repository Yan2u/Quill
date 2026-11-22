using System.Collections.Immutable;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>One line of the table of contents: the heading's key, level and shown text.</summary>
public sealed record TocEntry(string Key, int Level, string Text);

/// <summary>
/// The entries of a TOC block (Q68): every heading of the document in order. Equality and the 64-bit
/// hash are structural, so the TOC element is patched only when a heading changed (05).
/// </summary>
public sealed class TocContent : IStructuralHash, IEquatable<TocContent>
{
    public static TocContent Empty { get; } = new([]);

    public TocContent(ImmutableArray<TocEntry> entries)
    {
        Entries = entries;
        ulong hash = NodeHash.Seed;
        foreach (var entry in entries)
        {
            hash = NodeHash.Combine(hash, entry.Key);
            hash = NodeHash.Combine(hash, (ulong)(uint)entry.Level);
            hash = NodeHash.Combine(hash, entry.Text);
        }

        StructuralHash = hash;
    }

    public ImmutableArray<TocEntry> Entries { get; }

    public ulong StructuralHash { get; }

    public bool Equals(TocContent? other) =>
        other is not null && (ReferenceEquals(this, other) || (other.StructuralHash == StructuralHash && other.Entries.SequenceEqual(Entries)));

    public override bool Equals(object? obj) => Equals(obj as TocContent);

    public override int GetHashCode() => (int)StructuralHash;

    public override string ToString() => string.Join(" / ", Entries.Select(static entry => entry.Text));
}
