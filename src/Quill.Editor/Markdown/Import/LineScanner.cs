using System.Collections.Immutable;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Import;

/// <summary>
/// Containers that contribute a prefix to the lines they enclose.
/// </summary>
internal enum ChainKind
{
    BlockQuote,
    ListItem,
    Footnote,
}

/// <summary>
/// One enclosing container of a line, outermost first.
/// </summary>
/// <param name="OpeningLine">List item: the marker line. Footnote: the definition line.</param>
/// <param name="ContentWidth">List item: columns from the item's start to its content, measured on its marker line.</param>
/// <param name="OpensBlank">List item: the marker line has no content, so the whole line is the opening.</param>
internal sealed record ChainEntry(BlockId Id, ChainKind Kind, int OpeningLine = -1, int ContentWidth = 0, bool OpensBlank = false);

/// <summary>
/// The result of splitting a physical line along its container chain.
/// </summary>
/// <param name="Prefix">Segments to store (list item and footnote openings excluded: they are derived from the block).</param>
/// <param name="Body">Cursor positioned at the start of the line's body.</param>
/// <param name="LazyFrom">Index of the first chain entry the line did not match (lazy continuation), or the chain length.</param>
/// <param name="Openings">Opening text and content width of list items whose marker line this is, by chain index.</param>
/// <param name="ReachesItemContent">The innermost container is a list item and the line's indentation reaches its content column.</param>
internal readonly record struct LineScan(LinePrefix Prefix, LineCursor Body, int LazyFrom, (string Text, int Width)[]? Openings, bool ReachesItemContent = false)
{
    public string OpeningAt(int chainIndex) => Openings?[chainIndex].Text ?? string.Empty;

    public int OpeningWidthAt(int chainIndex) => Openings?[chainIndex].Width ?? 0;
}

/// <summary>
/// Splits physical lines into container prefix segments and a body, following CommonMark's container
/// matching. The block structure comes from Markdig; this only assigns the characters of each line.
/// </summary>
internal static class LineScanner
{
    /// <param name="allowLazy">
    /// The line may be a lazy continuation (CommonMark: only paragraph continuation text can be). On
    /// other lines a list item takes the indentation that is there even when it falls short of the
    /// item's content column, since the block structure (from Markdig) already places the line inside it.
    /// </param>
    public static LineScan Scan(SourceText source, int line, ImmutableArray<ChainEntry> chain, bool allowLazy = false)
    {
        var text = source.Text;
        var cursor = new LineCursor(text, source.LineStart(line), source.LineEnd(line));
        if (chain.IsEmpty)
        {
            return new LineScan(LinePrefix.None, cursor, 0, null);
        }

        var segments = ImmutableArray.CreateBuilder<PrefixSegment>(chain.Length);
        (string, int)[]? openings = null;
        int lazyFrom = chain.Length;
        bool reachesItemContent = false;
        for (int i = 0; i < chain.Length; i++)
        {
            var entry = chain[i];
            if (i >= lazyFrom)
            {
                segments.Add(new PrefixSegment(entry.Id, string.Empty));
                continue;
            }

            int segmentStart = cursor.Position;
            switch (entry.Kind)
            {
                case ChainKind.BlockQuote:
                    if (TryMatchQuoteMarker(ref cursor))
                    {
                        segments.Add(new PrefixSegment(entry.Id, text[segmentStart..cursor.Position]));
                    }
                    else
                    {
                        lazyFrom = i;
                        segments.Add(new PrefixSegment(entry.Id, string.Empty));
                    }

                    break;

                case ChainKind.ListItem when line == entry.OpeningLine:
                {
                    int width = ConsumeListItemOpening(ref cursor, entry.OpensBlank);
                    openings ??= new (string, int)[chain.Length];
                    openings[i] = (text[segmentStart..cursor.Position], width);
                    break;
                }

                case ChainKind.ListItem:
                {
                    var probe = cursor;
                    bool blank = probe.RestIsBlank();
                    int consumed = probe.ConsumeWhitespace(entry.ContentWidth);
                    reachesItemContent = i == chain.Length - 1 && consumed >= entry.ContentWidth;
                    if (blank || consumed >= entry.ContentWidth || !allowLazy)
                    {
                        cursor = probe;
                        segments.Add(new PrefixSegment(entry.Id, text[segmentStart..cursor.Position]));
                    }
                    else
                    {
                        lazyFrom = i;
                        segments.Add(new PrefixSegment(entry.Id, string.Empty));
                    }

                    break;
                }

                case ChainKind.Footnote when line == entry.OpeningLine:
                    // The definition's own syntax starts the body; it contributes no prefix.
                    break;

                case ChainKind.Footnote:
                    // Markdig consumes exactly four columns from indented continuation lines and nothing otherwise.
                    if (cursor.RestIsBlank() || cursor.MeasureIndentation() >= 4)
                    {
                        cursor.ConsumeWhitespace(4);
                    }

                    segments.Add(new PrefixSegment(entry.Id, text[segmentStart..cursor.Position]));
                    break;
            }
        }

        var prefix = segments.Count == 0 ? LinePrefix.None : new LinePrefix(segments.ToImmutable());
        return new LineScan(prefix, cursor, lazyFrom, openings, reachesItemContent && lazyFrom == chain.Length);
    }

    /// <summary>
    /// Consumes a list item opening by CommonMark's rule (5.2): indentation, the marker, then the
    /// spacing up to the content. One to four columns after the marker belong to it; five or more mean
    /// one column (the content is indented code). An item whose marker line holds nothing else either
    /// opens with a blank line (the whole line is the opening) or is empty (one column of spacing).
    /// Returns the content width: columns from where the item starts to its content.
    /// </summary>
    private static int ConsumeListItemOpening(ref LineCursor cursor, bool opensBlank)
    {
        int start = cursor.Column;
        cursor.ConsumeWhitespace(int.MaxValue);
        if (char.IsAsciiDigit(cursor.Current))
        {
            while (char.IsAsciiDigit(cursor.Current))
            {
                cursor.ConsumeChar();
            }
        }

        if (!cursor.AtEnd)
        {
            cursor.ConsumeChar(); // The bullet, or the delimiter after the digits.
        }

        int markerEnd = cursor.Column;
        if (opensBlank)
        {
            cursor.ConsumeRest();
            return markerEnd + 1 - start;
        }

        int spacing = cursor.MeasureIndentation();
        cursor.ConsumeWhitespace(cursor.RestIsBlank() || spacing >= 5 ? 1 : spacing);
        return cursor.Column - start;
    }

    /// <summary>Up to three columns of indentation, '&gt;', then one optional column of space or tab.</summary>
    private static bool TryMatchQuoteMarker(ref LineCursor cursor)
    {
        var probe = cursor;
        probe.ConsumeWhitespace(3);
        if (probe.Column != probe.PositionColumn || probe.Current != '>')
        {
            return false;
        }

        probe.ConsumeChar();
        if (!probe.AtEnd && probe.Current is ' ' or '\t')
        {
            probe.ConsumeOneColumn();
        }

        cursor = probe;
        return true;
    }
}
