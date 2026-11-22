using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Import;

/// <summary>
/// The bodies of consecutive physical lines joined with "\n", for blocks whose parts (slots and the
/// syntax between them) may span lines, such as link reference definitions.
/// </summary>
internal sealed class JoinedLines
{
    private readonly SourceText _source;
    private readonly int _firstLine;
    private readonly int[] _bodyStarts;
    private readonly int[] _bases;
    private readonly LinePrefix[] _prefixes;

    public JoinedLines(SourceText source, int firstLine, int lastLine, ImmutableArray<ChainEntry> chain)
    {
        _source = source;
        _firstLine = firstLine;
        int count = lastLine - firstLine + 1;
        _bodyStarts = new int[count];
        _bases = new int[count];
        _prefixes = new LinePrefix[count];
        var builder = new System.Text.StringBuilder();
        for (int i = 0; i < count; i++)
        {
            int line = firstLine + i;

            // Definitions are parsed from paragraph text, so their later lines may be lazy.
            var scan = LineScanner.Scan(source, line, chain, allowLazy: i > 0);
            if (i > 0)
            {
                builder.Append('\n');
            }

            _bodyStarts[i] = scan.Body.Position;
            _bases[i] = builder.Length;
            _prefixes[i] = scan.Prefix;
            builder.Append(source.Text, scan.Body.Position, source.LineEnd(line) - scan.Body.Position);
        }

        Text = builder.ToString();
    }

    public string Text { get; }

    /// <summary>Maps an absolute source offset to an offset in <see cref="Text"/>.</summary>
    public int Map(int absolute)
    {
        int index = Math.Clamp(_source.LineOf(absolute) - _firstLine, 0, _bases.Length - 1);
        int lineLength = (index + 1 < _bases.Length ? _bases[index + 1] - 1 : Text.Length) - _bases[index];
        return _bases[index] + Math.Clamp(absolute - _bodyStarts[index], 0, lineLength);
    }

    /// <summary>
    /// Splits <see cref="Text"/> at the given offsets. The prefix of each line goes to the part
    /// holding the line break before it; the first line's prefix goes to the first part.
    /// </summary>
    public JoinedPart[] Split(int[] bounds)
    {
        var clamped = new int[bounds.Length];
        for (int i = 0; i < bounds.Length; i++)
        {
            clamped[i] = Math.Clamp(bounds[i], i == 0 ? 0 : clamped[i - 1], Text.Length);
        }

        var parts = new JoinedPart[bounds.Length - 1];
        for (int p = 0; p < parts.Length; p++)
        {
            string text = Text[clamped[p]..clamped[p + 1]];
            var prefixes = new LinePrefix[text.Count(static c => c == '\n') + 1];
            if (p == 0)
            {
                prefixes[0] = _prefixes[0];
            }

            int ordinal = 0;
            for (int offset = 0; offset < text.Length; offset++)
            {
                if (text[offset] == '\n')
                {
                    ordinal++;
                    int line = Array.BinarySearch(_bases, clamped[p] + offset + 1);
                    if (line > 0)
                    {
                        prefixes[ordinal] = _prefixes[line];
                    }
                }
            }

            parts[p] = new JoinedPart(text, [.. prefixes]);
        }

        return parts;
    }
}

/// <summary>
/// A part of a <see cref="JoinedLines"/> text with the prefixes of the lines it starts.
/// </summary>
internal readonly record struct JoinedPart(string Text, ImmutableArray<LinePrefix> Prefixes)
{
    public SyntaxText ToSyntaxText() => new(Text, BlockConverter.CompactPrefixes(Prefixes));

    public DefinitionSlot ToSlot(BlockId id, DefinitionSlotKind kind) => new()
    {
        Id = id,
        SlotKind = kind,
        Text = Text,
        LinePrefixes = BlockConverter.CompactPrefixes(Prefixes),
    };
}
