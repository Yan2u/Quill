using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Quill.Playground.TextPrototype;

/// <summary>
/// An inline object of the prototype: a box the text flows around, drawn as a rounded rectangle.
/// DirectWrite places an inline object by its baseline; a centered object puts its middle on the
/// middle of the text (ascent and descent of the surrounding font).
/// </summary>
internal sealed partial class BoxInlineObject : ICanvasTextInlineObject
{
    private readonly ProtoObject _spec;
    private readonly float _textAscent;
    private readonly float _textDescent;

    public BoxInlineObject(ProtoObject spec, float textAscent, float textDescent)
    {
        _spec = spec;
        _textAscent = textAscent;
        _textDescent = textDescent;
    }

    /// <summary>The session the layout is being drawn into; inline objects get no drawing session from Win2D.</summary>
    public CanvasDrawingSession? Session { get; set; }

    public float Baseline => _spec.Align == ProtoObjectAlign.Center
        ? _spec.Height / 2 + (_textAscent - _textDescent) / 2
        : _spec.Baseline;

    public CanvasLineBreakCondition BreakAfter => CanvasLineBreakCondition.Neutral;

    public CanvasLineBreakCondition BreakBefore => CanvasLineBreakCondition.Neutral;

    public Rect DrawBounds => new(0, 0, _spec.Width, _spec.Height);

    public Size Size => new(_spec.Width, _spec.Height);

    public bool SupportsSideways => false;

    public void Draw(ICanvasTextRenderer textRenderer, Vector2 point, bool isSideways, bool isRightToLeft, object brush)
    {
        Session?.FillRoundedRectangle(point.X, point.Y, _spec.Width, _spec.Height, 3, 3, _spec.Fill);
    }
}
