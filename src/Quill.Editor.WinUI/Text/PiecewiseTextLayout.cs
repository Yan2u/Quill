using System.Collections.Immutable;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Text;

/// <summary>
/// Long raw text (code, math source, front matter, HTML; no inline objects, spans only for the colors
/// of highlighted code) laid out in
/// pieces of whole lines, each its own <see cref="BlockTextLayout"/> stacked from the top. The line
/// break between two pieces belongs to neither: a piece never ends with the break that separates it
/// from the next, so every piece lays out exactly as its lines would in one layout. An edit re-lays
/// out only the pieces it touches and keeps the others (09, rule 1: a key costs what it changed, not
/// what the block holds); drawing skips the pieces outside the invalidated region.
/// </summary>
internal sealed class PiecewiseTextLayout : ITextBlockLayout
{
    /// <summary>Lines per piece; raw text with more lines than this is laid out in pieces.</summary>
    public const int LinesPerPiece = 64;

    private readonly List<Piece> _pieces;
    private readonly int[] _firstLines;
    private readonly float[] _tops;

    private PiecewiseTextLayout(TextSource source, BlockTextFormat format, MdStyleSheet sheet, float width, List<Piece> pieces)
    {
        Source = source;
        Text = source.Text;
        Format = format;
        Sheet = sheet;
        Width = width;
        _pieces = pieces;
        _firstLines = new int[pieces.Count];
        _tops = new float[pieces.Count];
        int line = 0;
        float top = 0;
        float content = 0;
        for (int i = 0; i < pieces.Count; i++)
        {
            _firstLines[i] = line;
            _tops[i] = top;
            line += pieces[i].Layout.LineCount;
            top += pieces[i].Layout.Height;
            content = Math.Max(content, pieces[i].Layout.ContentWidth);
        }

        LineCount = line;
        Height = top;
        ContentWidth = content;
    }

    public string Text { get; }

    private TextSource Source { get; }

    public float Width { get; }

    public float Height { get; }

    public float ContentWidth { get; }

    public int LineCount { get; }

    public float FirstBaseline => _pieces[0].Layout.FirstBaseline;

    /// <summary>The number of pieces, for tests and measurements.</summary>
    public int PieceCount => _pieces.Count;

    /// <summary>How many pieces the last <see cref="Create"/> laid out anew (the others were reused).</summary>
    public int LaidOutPieces { get; private set; }

    private BlockTextFormat Format { get; }

    private MdStyleSheet Sheet { get; }

    /// <summary>Whether text this long is worth laying out in pieces.</summary>
    public static bool Suits(string text)
    {
        int lines = 0;
        foreach (char c in text)
        {
            if (c == '\n' && ++lines > LinesPerPiece)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Lays out <paramref name="source"/>. Pieces of <paramref name="previous"/> (the layout before an edit,
    /// at the same width and format) that the edit did not touch, in text and in colors, are taken over;
    /// the previous layout must not be used afterwards, this one owns or disposed all its pieces.
    /// </summary>
    public static PiecewiseTextLayout Create(ICanvasResourceCreator creator, TextSource source, BlockTextFormat format, MdStyleSheet sheet, float width, PiecewiseTextLayout? previous)
    {
        string text = source.Text;
        if (previous is null || previous.Format != format || !ReferenceEquals(previous.Sheet, sheet) || Math.Abs(previous.Width - width) > 0.01f)
        {
            previous?.Dispose();
            var all = Split(creator, source, 0, text.Length, format, sheet, width);
            return new PiecewiseTextLayout(source, format, sheet, width, all) { LaidOutPieces = all.Count };
        }

        // The changed range: everything between the common prefix and the common suffix, of the text
        // and of the spans (an edit can recolor lines it did not touch, such as an opened comment).
        string old = previous.Text;
        int prefix = old.AsSpan().CommonPrefixLength(text);
        int limit = Math.Min(old.Length, text.Length) - prefix;
        int suffix = CommonSuffixLength(old.AsSpan(prefix), text.AsSpan(prefix), limit);
        int delta = text.Length - old.Length;
        var (spanPrefix, spanSuffix) = SpanChange(previous.Source.Spans, source.Spans, old.Length, delta);
        prefix = Math.Min(prefix, spanPrefix);
        suffix = Math.Max(0, Math.Min(Math.Min(suffix, spanSuffix), Math.Min(old.Length, text.Length) - prefix));

        // The pieces holding the start and the end of the change; a change at the break between two
        // pieces touches the piece before it, a change that removes the break touches both.
        int first = previous.PieceAt(prefix);
        int last = previous.PieceAt(old.Length - suffix);
        var pieces = new List<Piece>(previous._pieces.Count + 2);
        pieces.AddRange(previous._pieces.Take(first));
        int start = previous._pieces[first].Start;
        int end = previous._pieces[last].End + delta;
        var rebuilt = Split(creator, source, start, end, format, sheet, width);
        pieces.AddRange(rebuilt);
        foreach (var piece in previous._pieces.Skip(last + 1))
        {
            pieces.Add(piece with { Start = piece.Start + delta });
        }

        for (int i = first; i <= last; i++)
        {
            previous._pieces[i].Layout.Dispose();
        }

        previous._pieces.Clear();
        return new PiecewiseTextLayout(source, format, sheet, width, pieces) { LaidOutPieces = rebuilt.Count };
    }

    public void Draw(CanvasDrawingSession session, Vector2 origin, (int Start, int End)? selection = null, (int Start, int End)? composition = null, Rect? visible = null, IReadOnlyList<TextMark>? marks = null)
    {
        int from = 0;
        int to = _pieces.Count - 1;
        if (visible is { } region)
        {
            from = PieceAtY(region.Y - origin.Y);
            to = PieceAtY(region.Y + region.Height - origin.Y);
        }

        for (int i = from; i <= to; i++)
        {
            var piece = _pieces[i];
            var local = marks?.Select(mark => (Mark: mark, Range: Local(piece, (mark.Start, mark.End)))).Where(static pair => pair.Range is not null)
                .Select(static pair => new TextMark(pair.Range!.Value.Start, pair.Range.Value.End, pair.Mark.Current)).ToList();
            piece.Layout.Draw(session, origin + new Vector2(0, _tops[i]), Local(piece, selection), Local(piece, composition), marks: local);
        }
    }

    public Rect CaretRect(int offset, bool backward)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        int index = PieceAt(offset);
        return Shift(_pieces[index].Layout.CaretRect(offset - _pieces[index].Start, backward), _tops[index]);
    }

    public (int Start, int End, float Top, float Height) Line(int index)
    {
        index = Math.Clamp(index, 0, LineCount - 1);
        int piece = Search(_firstLines, index);
        var line = _pieces[piece].Layout.Line(index - _firstLines[piece]);
        int start = _pieces[piece].Start;
        return (line.Start + start, line.End + start, line.Top + _tops[piece], line.Height);
    }

    public int LineIndexOfCaret(int offset, bool backward)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        int index = PieceAt(offset);
        return _firstLines[index] + _pieces[index].Layout.LineIndexOfCaret(offset - _pieces[index].Start, backward);
    }

    public int LineIndexAt(double y)
    {
        int index = PieceAtY(y);
        return _firstLines[index] + _pieces[index].Layout.LineIndexAt(y - _tops[index]);
    }

    public (int Offset, bool AtWrappedLineEnd) HitTestLine(Vector2 point)
    {
        int index = PieceAtY(point.Y);
        var (offset, wrapped) = _pieces[index].Layout.HitTestLine(point - new Vector2(0, _tops[index]));
        return (offset + _pieces[index].Start, wrapped);
    }

    public int HitTest(Vector2 point) => HitTestLine(point).Offset;

    public IReadOnlyList<Rect> Regions(int start, int end)
    {
        var regions = new List<Rect>();
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, start, Text.Length);
        if (end == start)
        {
            return regions;
        }

        for (int i = PieceAt(start); i < _pieces.Count && _pieces[i].Start < end; i++)
        {
            var piece = _pieces[i];
            int localStart = Math.Max(0, start - piece.Start);
            int localEnd = Math.Min(piece.Length, end - piece.Start);
            if (localEnd > localStart)
            {
                regions.AddRange(piece.Layout.Regions(localStart, localEnd).Select(region => Shift(region, _tops[i])));
            }
        }

        return regions;
    }

    public (float Top, float Baseline, float Height) LineOf(int character)
    {
        int index = PieceAt(Math.Clamp(character, 0, Text.Length));
        var (top, baseline, height) = _pieces[index].Layout.LineOf(character - _pieces[index].Start);
        return (top + _tops[index], baseline, height);
    }

    public Point EndOfText()
    {
        var caret = CaretRect(Text.Length, backward: true);
        return new Point(caret.X, caret.Y);
    }

    public void Dispose()
    {
        foreach (var piece in _pieces)
        {
            piece.Layout.Dispose();
        }

        _pieces.Clear();
    }

    /// <summary>Lays out <c>text[start..end]</c>, which starts and ends at piece boundaries, in pieces of whole lines.</summary>
    private static List<Piece> Split(ICanvasResourceCreator creator, TextSource source, int start, int end, BlockTextFormat format, MdStyleSheet sheet, float width)
    {
        string text = source.Text;
        var pieces = new List<Piece>();
        int pieceStart = start;
        int lines = 0;
        for (int i = start; i < end; i++)
        {
            if (text[i] == '\n' && ++lines == LinesPerPiece)
            {
                pieces.Add(Lay(pieceStart, i));
                pieceStart = i + 1;
                lines = 0;
            }
        }

        pieces.Add(Lay(pieceStart, end));
        return pieces;

        Piece Lay(int from, int to) =>
            new(from, to - from, new BlockTextLayout(creator, Slice(source, from, to), format, sheet, width));
    }

    /// <summary><c>source[from..to]</c> with the spans clipped to it.</summary>
    private static TextSource Slice(TextSource source, int from, int to)
    {
        if (source.Spans.IsEmpty)
        {
            return TextSource.Plain(source.Text[from..to]);
        }

        var spans = ImmutableArray.CreateBuilder<TextSpan>();
        foreach (var span in source.Spans)
        {
            int start = Math.Max(span.Start, from);
            int end = Math.Min(span.Start + span.Length, to);
            if (end > start)
            {
                spans.Add(span with { Start = start - from, Length = end - start });
            }
        }

        return new TextSource(source.Text[from..to], spans.ToImmutable(), []);
    }

    /// <summary>
    /// How much of the text the spans leave unchanged: the offset where the first differing span starts
    /// (past the end when none differ) and the length of the end whose spans, shifted by
    /// <paramref name="delta"/>, are the same. Text under equal spans is compared separately.
    /// </summary>
    private static (int Prefix, int Suffix) SpanChange(ImmutableArray<TextSpan> old, ImmutableArray<TextSpan> spans, int oldLength, int delta)
    {
        int count = Math.Min(old.Length, spans.Length);
        int front = 0;
        while (front < count && old[front] == spans[front])
        {
            front++;
        }

        int prefix = Math.Min(front < old.Length ? old[front].Start : int.MaxValue, front < spans.Length ? spans[front].Start : int.MaxValue);

        int back = 0;
        while (back < count - front && old[^(back + 1)] == spans[^(back + 1)] with { Start = spans[^(back + 1)].Start - delta })
        {
            back++;
        }

        int suffix = int.MaxValue;
        if (back < old.Length || back < spans.Length)
        {
            int end = Math.Max(back < old.Length ? old[^(back + 1)].Start + old[^(back + 1)].Length : 0,
                back < spans.Length ? spans[^(back + 1)].Start + spans[^(back + 1)].Length - delta : 0);
            suffix = Math.Max(0, oldLength - end);
        }

        return (prefix, suffix);
    }

    /// <summary>The length of the common end of two texts, at most <paramref name="limit"/>; compares blocks with vectorized equality.</summary>
    private static int CommonSuffixLength(ReadOnlySpan<char> left, ReadOnlySpan<char> right, int limit)
    {
        const int Block = 256;
        int length = 0;
        while (length + Block <= limit && left[^(length + Block)..^length].SequenceEqual(right[^(length + Block)..^length]))
        {
            length += Block;
        }

        while (length < limit && left[^(length + 1)] == right[^(length + 1)])
        {
            length++;
        }

        return length;
    }

    private static Rect Shift(Rect rect, float y) => new(rect.X, rect.Y + y, rect.Width, rect.Height);

    private static (int Start, int End)? Local(Piece piece, (int Start, int End)? range)
    {
        if (range is not { } value)
        {
            return null;
        }

        int start = Math.Max(0, value.Start - piece.Start);
        int end = Math.Min(piece.Length, value.End - piece.Start);
        return end > start ? (start, end) : null;
    }

    /// <summary>The last index whose value is at most <paramref name="value"/> (0 when none).</summary>
    private static int Search(int[] values, int value)
    {
        int low = 0, high = values.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (values[middle] <= value)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>The piece holding an offset: an offset at the break between two pieces ends the first.</summary>
    private int PieceAt(int offset)
    {
        int low = 0, high = _pieces.Count - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_pieces[middle].Start <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private int PieceAtY(double y)
    {
        int low = 0, high = _tops.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (_tops[middle] <= y)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    /// <summary>A piece: <c>Text[Start..End]</c>, without the line break that follows it.</summary>
    private sealed record Piece(int Start, int Length, BlockTextLayout Layout)
    {
        public int End => Start + Length;
    }
}
