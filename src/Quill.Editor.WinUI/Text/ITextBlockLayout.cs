using System.Numerics;
using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Text;

/// <summary>A find match in display offsets; <see cref="Current"/> for the match the selection is.</summary>
internal readonly record struct TextMark(int Start, int End, bool Current);

/// <summary>
/// The laid out text of a block, in the block's text coordinates: what the element draws and what the
/// layout service asks (caret rectangles, visual lines, hit tests, range regions). Offsets are display
/// offsets (05). Implemented by <see cref="BlockTextLayout"/> (one DirectWrite layout) and by
/// <see cref="PiecewiseTextLayout"/> (long raw text in pieces, so an edit lays out only what changed).
/// </summary>
internal interface ITextBlockLayout : IDisposable
{
    string Text { get; }

    float Width { get; }

    float Height { get; }

    /// <summary>The baseline of the first line from the top: list markers align with it.</summary>
    float FirstBaseline { get; }

    /// <summary>The width of the widest line.</summary>
    float ContentWidth { get; }

    /// <summary>The number of visual lines (an empty block has one).</summary>
    int LineCount { get; }

    /// <summary>
    /// Draws the text at <paramref name="origin"/>; <paramref name="visible"/>, in the same coordinates
    /// as the origin, limits drawing to what intersects it when the layout can skip the rest.
    /// </summary>
    /// <param name="marks">Find matches (12-find-replace.md) in display offsets, drawn behind the selection.</param>
    void Draw(CanvasDrawingSession session, Vector2 origin, (int Start, int End)? selection = null, (int Start, int End)? composition = null, Rect? visible = null, IReadOnlyList<TextMark>? marks = null);

    /// <summary>The caret at a display offset (03: affinity decides between the two edges of a wrap).</summary>
    Rect CaretRect(int offset, bool backward);

    /// <summary>A visual line: its first and last caret offsets and its vertical extent.</summary>
    (int Start, int End, float Top, float Height) Line(int index);

    /// <summary>The visual line of a caret, with the same affinity rule as <see cref="CaretRect"/>.</summary>
    int LineIndexOfCaret(int offset, bool backward);

    /// <summary>The visual line at a vertical position (clamped to the first and last line).</summary>
    int LineIndexAt(double y);

    /// <summary>The caret offset nearest to a point, and whether it is the end of a wrapped line.</summary>
    (int Offset, bool AtWrappedLineEnd) HitTestLine(Vector2 point);

    /// <summary>The rectangles covering a range of the text, one per line.</summary>
    IReadOnlyList<Rect> Regions(int start, int end);

    /// <summary>The top, baseline (from the top) and height of the line holding a character.</summary>
    (float Top, float Baseline, float Height) LineOf(int character);

    /// <summary>The caret offset nearest to a point.</summary>
    int HitTest(Vector2 point) => HitTestLine(point).Offset;

    /// <summary>The right end of the last line: where decorations after the text go (footnote backlinks).</summary>
    Point EndOfText()
    {
        var caret = CaretRect(Text.Length, backward: true);
        return new Point(caret.X, caret.Y);
    }
}
