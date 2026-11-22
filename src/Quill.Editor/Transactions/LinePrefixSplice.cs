using System.Collections.Immutable;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Transactions;

/// <summary>
/// Keeps a leaf's per-line prefix records aligned with its text when a range is replaced: lines whose
/// start lies in the replaced range lose their records; lines started by the inserted text get the
/// given records, or none (generated on export, 06-markdown-io.md).
/// </summary>
internal static class LinePrefixSplice
{
    /// <returns>The new records and the removed ones (for the inverse step).</returns>
    public static (ImmutableArray<LinePrefix> Prefixes, ImmutableArray<LinePrefix> Removed) Splice(
        string text,
        ImmutableArray<LinePrefix> prefixes,
        int from,
        int to,
        string inserted,
        ImmutableArray<LinePrefix> insertedPrefixes)
    {
        int firstLine = Count(text, 0, from);
        int removedCount = Count(text, from, to);
        int insertedCount = Count(inserted, 0, inserted.Length);
        bool hasInserted = !insertedPrefixes.IsDefaultOrEmpty && insertedPrefixes.Any(static prefix => !prefix.IsNone);
        if (prefixes.IsEmpty && !hasInserted)
        {
            return ([], []);
        }

        var removed = ImmutableArray.CreateBuilder<LinePrefix>(removedCount);
        for (int line = firstLine + 1; line <= firstLine + removedCount; line++)
        {
            removed.Add(At(prefixes, line));
        }

        int lineCount = Count(text, 0, text.Length) + 1;
        var result = new List<LinePrefix>(lineCount - removedCount + insertedCount);
        for (int line = 0; line <= firstLine; line++)
        {
            result.Add(At(prefixes, line));
        }

        for (int i = 0; i < insertedCount; i++)
        {
            result.Add(hasInserted && i < insertedPrefixes.Length ? insertedPrefixes[i] : LinePrefix.None);
        }

        for (int line = firstLine + removedCount + 1; line < lineCount; line++)
        {
            result.Add(At(prefixes, line));
        }

        bool any = result.Any(static prefix => !prefix.IsNone);
        return (any ? [.. result] : [], removed.MoveToImmutable());
    }

    /// <summary>The records with the one of <paramref name="line"/> replaced; empty when none is left.</summary>
    public static ImmutableArray<LinePrefix> Set(ImmutableArray<LinePrefix> prefixes, int line, LinePrefix prefix)
    {
        var builder = prefixes.ToBuilder();
        while (builder.Count <= line)
        {
            builder.Add(LinePrefix.None);
        }

        builder[line] = prefix;
        return builder.Any(static record => !record.IsNone) ? builder.ToImmutable() : [];
    }

    /// <summary>Whether the line holding <paramref name="offset"/> has only spaces and tabs.</summary>
    public static bool IsBlankLine(string text, int offset)
    {
        int start = offset == 0 ? 0 : text.LastIndexOf('\n', offset - 1) + 1;
        int end = text.IndexOf('\n', offset);
        return text.AsSpan(start, (end < 0 ? text.Length : end) - start).TrimStart(" \t").IsEmpty;
    }

    private static LinePrefix At(ImmutableArray<LinePrefix> prefixes, int line) =>
        line < prefixes.Length ? prefixes[line] : LinePrefix.None;

    private static int Count(string text, int start, int end)
    {
        int count = 0;
        for (int i = start; i < end; i++)
        {
            if (text[i] == '\n')
            {
                count++;
            }
        }

        return count;
    }
}
