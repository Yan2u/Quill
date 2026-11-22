using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Quill.Playground.TextPrototype;

/// <summary>
/// Draws only the glyph runs of superscript and subscript ranges, shifted off the baseline.
/// DirectWrite has no baseline shift, so the main pass draws those ranges transparent and this
/// renderer draws them again where they belong.
/// </summary>
internal sealed partial class ShiftedRunRenderer : ICanvasTextRenderer
{
    private readonly CanvasDrawingSession _session;
    private readonly IReadOnlyList<(int Start, int End, float Shift, Color Color)> _ranges;

    public ShiftedRunRenderer(CanvasDrawingSession session, IReadOnlyList<(int Start, int End, float Shift, Color Color)> ranges)
    {
        _session = session;
        _ranges = ranges;
    }

    public float Dpi => _session.Dpi;

    public bool PixelSnappingDisabled => false;

    public Matrix3x2 Transform => _session.Transform;

    public void DrawGlyphRun(Vector2 point, CanvasFontFace fontFace, float fontSize, CanvasGlyph[] glyphs, bool isSideways, uint bidiLevel, object brush,
        CanvasTextMeasuringMode measuringMode, string localeName, string textString, int[] clusterMapIndices, uint characterIndex, CanvasGlyphOrientation glyphOrientation)
    {
        foreach (var (start, end, shift, color) in _ranges)
        {
            if (characterIndex >= start && characterIndex < end)
            {
                using var solid = new Microsoft.Graphics.Canvas.Brushes.CanvasSolidColorBrush(_session, color);
                _session.DrawGlyphRun(point + new Vector2(0, shift), fontFace, fontSize, glyphs, isSideways, bidiLevel, solid, measuringMode);
                return;
            }
        }
    }

    public void DrawInlineObject(Vector2 baselineOrigin, ICanvasTextInlineObject inlineObject, bool isSideways, bool isRightToLeft, object brush, CanvasGlyphOrientation glyphOrientation)
    {
    }

    public void DrawStrikethrough(Vector2 point, float strikethroughWidth, float strikethroughThickness, float strikethroughOffset, CanvasTextDirection textDirection, object brush,
        CanvasTextMeasuringMode textMeasuringMode, string localeName, CanvasGlyphOrientation glyphOrientation)
    {
    }

    public void DrawUnderline(Vector2 point, float underlineWidth, float underlineThickness, float underlineOffset, float runHeight, CanvasTextDirection textDirection, object brush,
        CanvasTextMeasuringMode textMeasuringMode, string localeName, CanvasGlyphOrientation glyphOrientation)
    {
    }
}
