using System.Runtime.CompilerServices;

namespace Quill.VariableXaml.Model;

/// <summary>
/// 64-bit structural hashing helpers for virtual nodes. Hashes are only compared within one process.
/// Typed values hash with <see cref="IStructuralHash"/> when they provide it, otherwise with
/// <see cref="object.GetHashCode"/> (acceptable for small values such as numbers and enums).
/// </summary>
public static class NodeHash
{
    public const ulong Seed = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const ulong NullMarker = 0x5bd1e9955bd1e995UL;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Combine(ulong hash, ulong value)
    {
        hash ^= value + 0x9E3779B97F4A7C15UL + (hash << 6) + (hash >> 2);
        return hash;
    }

    public static ulong Combine(ulong hash, string? text)
    {
        if (text is null)
        {
            return Combine(hash, NullMarker);
        }

        // FNV over four characters at a time: long texts (a code block's source) hash on every edit.
        ulong fnv = Seed;
        var span = text.AsSpan();
        var words = System.Runtime.InteropServices.MemoryMarshal.Cast<char, ulong>(span);
        foreach (ulong word in words)
        {
            fnv ^= word;
            fnv *= FnvPrime;
            fnv ^= fnv >> 29;
        }

        foreach (char c in span[(words.Length * 4)..])
        {
            fnv ^= c;
            fnv *= FnvPrime;
        }

        return Combine(hash, fnv ^ (ulong)text.Length);
    }

    public static ulong Combine(ulong hash, object? value) => value switch
    {
        null => Combine(hash, NullMarker),
        string text => Combine(hash, text),
        IStructuralHash structural => Combine(Combine(hash, TypeHash(value)), structural.StructuralHash),
        _ => Combine(Combine(hash, TypeHash(value)), (ulong)(uint)value.GetHashCode()),
    };

    public static ulong Combine(ulong hash, XamlName name) =>
        Combine(Combine(hash, name.Namespace), name.LocalName);

    public static ulong Combine(ulong hash, XamlMemberName name) =>
        Combine(Combine(Combine(Combine(hash, (ulong)name.Kind), name.OwnerNamespace), name.OwnerLocalName), name.Member);

    // The type name rather than Type.GetHashCode, so hashes do not depend on the runtime's type handles.
    private static ulong TypeHash(object value) => Combine(Seed, value.GetType().FullName);
}
