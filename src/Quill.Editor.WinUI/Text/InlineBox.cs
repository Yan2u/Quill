using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Text;

/// <summary>How an inline object sits on its line (10-elements.md).</summary>
internal enum InlineBoxAlignment
{
    /// <summary>Its own baseline on the text baseline: inline math.</summary>
    Baseline,

    /// <summary>Its middle on the middle of the text: images next to text (Typora).</summary>
    Center,
}

/// <summary>
/// An atomic inline object in the text flow (math, image, emoji, entity): one character of the
/// displayed text (U+FFFC), sized by its content and drawn by a callback. DirectWrite places an
/// object by its baseline; a centered object puts its middle on the middle of the surrounding text.
/// <para>
/// Lines are spaced proportionally (CSS line-height), which would add leading to an image as well: a
/// tall image got blank bands above and below it (user report, 2026-11-03). An object with
/// <c>leading</c> &gt; 1 reports its height and baseline divided by it, so the scaled line is as tall as
/// the image, and is drawn at its real size with its baseline where the line's baseline is, as CSS does
/// for replaced elements.
/// </para>
/// </summary>
internal sealed partial class InlineBox : ICanvasTextInlineObject
{
    private readonly float _textAscent;
    private readonly float _textDescent;
    private readonly float _ownBaseline;
    private readonly Action<CanvasDrawingSession, Rect> _draw;
    private readonly float _leading;

    /// <param name="leading">The proportional line spacing the object is exempt from; 1 for none.</param>
    public InlineBox(float width, float height, InlineBoxAlignment alignment, float ownBaseline, float textAscent, float textDescent, Action<CanvasDrawingSession, Rect> draw, float leading = 1)
    {
        Width = width;
        RealHeight = height;
        Alignment = alignment;
        _ownBaseline = ownBaseline;
        _textAscent = textAscent;
        _textDescent = textDescent;
        _draw = draw;
        _leading = Math.Max(1, leading);
    }

    public float Width { get; }

    /// <summary>The height DirectWrite lays out with: the real height without the line's leading.</summary>
    public float Height => RealHeight / _leading;

    /// <summary>The height the object is drawn at.</summary>
    public float RealHeight { get; }

    /// <summary>The real baseline, from the object's top.</summary>
    private float RealBaseline => Alignment == InlineBoxAlignment.Center ? RealHeight / 2 + (_textAscent - _textDescent) / 2 : _ownBaseline;

    public InlineBoxAlignment Alignment { get; }

    /// <summary>The session the layout is drawn into: Win2D hands inline objects no session.</summary>
    public CanvasDrawingSession? Session { get; set; }

    public float Baseline => RealBaseline / _leading;

    public CanvasLineBreakCondition BreakAfter => CanvasLineBreakCondition.Neutral;

    public CanvasLineBreakCondition BreakBefore => CanvasLineBreakCondition.Neutral;

    public Rect DrawBounds => new(0, Baseline - RealBaseline, Width, RealHeight);

    public Size Size => new(Width, Height);

    public bool SupportsSideways => false;

    public void Draw(ICanvasTextRenderer textRenderer, Vector2 point, bool isSideways, bool isRightToLeft, object brush)
    {
        if (Session is { } session)
        {
            // DirectWrite put the reported baseline on the line's: the real one goes there.
            _draw(session, new Rect(point.X, point.Y + Baseline - RealBaseline, Width, RealHeight));
        }
    }
}
