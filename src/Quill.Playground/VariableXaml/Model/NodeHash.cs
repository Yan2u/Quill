using System.Runtime.CompilerServices;

namespace Quill.Playground.VariableXaml.Model;

/// <summary>
/// 64-bit structural hashing helpers for virtual nodes. Hashes are only compared within one
/// process, so <see cref="object.GetHashCode"/> is acceptable for typed (non-string) values.
/// </summary>
internal static class NodeHash
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

        ulong fnv = Seed;
        foreach (char c in text)
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
        _ => Combine(Combine(hash, (ulong)(uint)value.GetType().GetHashCode()), (ulong)(uint)value.GetHashCode()),
    };

    public static ulong Combine(ulong hash, XamlName name) =>
        Combine(Combine(hash, name.Namespace), name.LocalName);

    public static ulong Combine(ulong hash, XamlMemberName name) =>
        Combine(Combine(Combine(Combine(hash, (ulong)name.Kind), name.OwnerNamespace), name.OwnerLocalName), name.Member);
}
