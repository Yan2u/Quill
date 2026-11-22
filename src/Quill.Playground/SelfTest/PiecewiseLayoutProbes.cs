using System.Numerics;
using System.Text;
using Microsoft.Graphics.Canvas;
using Quill.Editor.WinUI.Text;
using Quill.Editor.WinUI.Theming;

namespace Quill.Playground.SelfTest;

/// <summary>
/// M5.6: long raw text laid out in pieces (<see cref="PiecewiseTextLayout"/>) answers exactly like one
/// layout of the whole text (lines, carets, hit tests, regions, height), wrapped lines included; an
/// edit lays out only the pieces it touches. Needs only a Win2D device, no element tree.
/// </summary>
internal static class PiecewiseLayoutProbes
{
    private const float Width = 420;

    public static Task RunAsync(SelfTestContext context)
    {
        var device = CanvasDevice.GetSharedDevice();
        var sheet = MdStyleSheet.Light;
        var format = new BlockTextFormat(sheet.FontFamily, sheet.FontSize * sheet.CodeScale, sheet.Foreground, Monospace: true);
        string text = Sample(300);

        using var whole = new BlockTextLayout(device, TextSource.Plain(text), format, sheet, Width);
        var pieces = PiecewiseTextLayout.Create(device, TextSource.Plain(text), format, sheet, Width, previous: null);
        context.Check(pieces.PieceCount == 5 && pieces.LineCount == whole.LineCount && Math.Abs(pieces.Height - whole.Height) < 0.5,
            "piecewise: 300 lines in pieces of 64 have the lines and height of one layout",
            $"{pieces.PieceCount} pieces, lines {pieces.LineCount} / {whole.LineCount}, height {pieces.Height:F1} / {whole.Height:F1}");

        context.Check(Compare(whole, pieces, text) is null, "piecewise: lines, carets (both affinities), hit tests and regions match one layout", Compare(whole, pieces, text));

        // Type into the middle, then add and remove line breaks: only the touched pieces are laid out again.
        int middle = text.IndexOf('\n', text.Length / 2);
        string typed = text.Insert(middle, "x");
        pieces = PiecewiseTextLayout.Create(device, TextSource.Plain(typed), format, sheet, Width, pieces);
        int typing = pieces.LaidOutPieces;
        using (var check = new BlockTextLayout(device, TextSource.Plain(typed), format, sheet, Width))
        {
            context.Check(typing == 1 && Compare(check, pieces, typed) is null, "piecewise: typing in the middle lays out one piece and still matches", $"{typing} laid out; {Compare(check, pieces, typed)}");
        }

        string broken = typed.Insert(middle, "\n\n");
        pieces = PiecewiseTextLayout.Create(device, TextSource.Plain(broken), format, sheet, Width, pieces);
        using (var check = new BlockTextLayout(device, TextSource.Plain(broken), format, sheet, Width))
        {
            context.Check(pieces.LaidOutPieces <= 2 && Compare(check, pieces, broken) is null, "piecewise: new line breaks split the touched piece only", $"{pieces.LaidOutPieces} laid out, {pieces.PieceCount} pieces; {Compare(check, pieces, broken)}");
        }

        // Removing the break between two pieces joins them.
        int boundary = Boundary(broken, pieces);
        string joined = broken.Remove(boundary, 1);
        pieces = PiecewiseTextLayout.Create(device, TextSource.Plain(joined), format, sheet, Width, pieces);
        using (var check = new BlockTextLayout(device, TextSource.Plain(joined), format, sheet, Width))
        {
            context.Check(Compare(check, pieces, joined) is null, "piecewise: deleting the break between two pieces still matches", Compare(check, pieces, joined));
        }

        string ended = joined + "\n";
        pieces = PiecewiseTextLayout.Create(device, TextSource.Plain(ended), format, sheet, Width, pieces);
        using (var check = new BlockTextLayout(device, TextSource.Plain(ended), format, sheet, Width))
        {
            context.Check(Compare(check, pieces, ended) is null && pieces.LaidOutPieces == 1, "piecewise: a line break at the end adds the empty last line", Compare(check, pieces, ended));
        }

        pieces.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>The first difference between the two layouts, or null.</summary>
    private static string? Compare(BlockTextLayout whole, PiecewiseTextLayout pieces, string text)
    {
        if (whole.LineCount != pieces.LineCount)
        {
            return $"line count {pieces.LineCount} / {whole.LineCount}";
        }

        for (int i = 0; i < whole.LineCount; i++)
        {
            var (start, end, top, height) = whole.Line(i);
            var other = pieces.Line(i);
            if (other.Start != start || other.End != end || Math.Abs(other.Top - top) > 0.5 || Math.Abs(other.Height - height) > 0.5)
            {
                return $"line {i}: {other} / {(start, end, top, height)}";
            }

            float y = top + height / 2;
            foreach (float x in new[] { -5f, 10f, 200f, Width + 20 })
            {
                var expected = whole.HitTestLine(new Vector2(x, y));
                var actual = pieces.HitTestLine(new Vector2(x, y));
                if (expected != actual)
                {
                    return $"hit test at ({x}, {y:F1}): {actual} / {expected}";
                }
            }

            if (pieces.LineIndexAt(y) != i || whole.LineIndexAt(y) != i)
            {
                return $"line at {y:F1}: {pieces.LineIndexAt(y)} / {whole.LineIndexAt(y)}, expected {i}";
            }
        }

        for (int offset = 0; offset <= text.Length; offset += offset % 97 == 0 ? 1 : 13)
        {
            foreach (bool backward in new[] { false, true })
            {
                var expected = whole.CaretRect(offset, backward);
                var actual = pieces.CaretRect(offset, backward);
                if (Math.Abs(expected.X - actual.X) > 0.5 || Math.Abs(expected.Y - actual.Y) > 0.5 || pieces.LineIndexOfCaret(offset, backward) != whole.LineIndexOfCaret(offset, backward))
                {
                    return $"caret {offset}{(backward ? "<" : string.Empty)}: {actual} / {expected}";
                }
            }
        }

        var regions = pieces.Regions(text.Length / 3, text.Length / 3 + 500);
        var wholeRegions = whole.Regions(text.Length / 3, text.Length / 3 + 500);
        double area = regions.Sum(rect => rect.Width * rect.Height);
        double wholeArea = wholeRegions.Sum(rect => rect.Width * rect.Height);
        if (Math.Abs(area - wholeArea) > wholeArea * 0.05)
        {
            return $"regions cover {area:F0} / {wholeArea:F0}";
        }

        return Math.Abs(pieces.Height - whole.Height) > 0.5 ? $"height {pieces.Height} / {whole.Height}" : null;
    }

    /// <summary>The offset of a line break between two pieces: the end of the first piece's last line.</summary>
    private static int Boundary(string text, PiecewiseTextLayout pieces)
    {
        int lines = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n' && ++lines == PiecewiseTextLayout.LinesPerPiece)
            {
                return i;
            }
        }

        return text.Length / 2;
    }

    private static string Sample(int lines)
    {
        var text = new StringBuilder();
        for (int i = 0; i < lines; i++)
        {
            text.Append(i % 17 == 5 ? string.Empty : new string(' ', 4 * (i % 3)) + $"value{i} = Compute({i}, \"text\");");
            if (i % 23 == 7)
            {
                // A line long enough to wrap at the layout width.
                text.Append(" // " + string.Concat(Enumerable.Repeat("wrapping comment ", 8)));
            }

            if (i < lines - 1)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }
}
