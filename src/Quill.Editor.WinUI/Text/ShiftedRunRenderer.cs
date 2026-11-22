using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Text;
using Windows.UI;

namespace Quill.Editor.WinUI.Text;

/// <summary>A range of the text drawn shifted off the baseline, in its own color.</summary>
internal readonly record struct ShiftedRange(int Start, int End, float Shift, Color Color);

/// <summary>
/// Draws only the glyph runs of superscript and subscript ranges, shifted off the baseline.
/// DirectWrite has no baseline shift, so the main pass draws those ranges transparent and this
/// renderer draws them again where they belong (Q8 prototype). With a color for all runs it draws
/// every run in that color instead, shifted ones where they belong: selected text in a style sheet
/// with a selection foreground, drawn again under a clip to the selection.
/// </summary>
internal sealed partial class ShiftedRunRenderer : ICanvasTextRenderer
{
    private readonly CanvasDrawingSession _session;
    private readonly IReadOnlyList<ShiftedRange> _ranges;
    private readonly Color? _all;

    public ShiftedRunRenderer(CanvasDrawingSession session, IReadOnlyList<ShiftedRange> ranges, Color? all = null)
    {
        _session = session;
        _ranges = ranges;
        _all = all;
    }

    public float Dpi => _session.Dpi;

    public bool PixelSnappingDisabled => false;

    public Matrix3x2 Transform => _session.Transform;

    public void DrawGlyphRun(Vector2 point, CanvasFontFace fontFace, float fontSize, CanvasGlyph[] glyphs, bool isSideways, uint bidiLevel, object brush,
        CanvasTextMeasuringMode measuringMode, string localeName, string textString, int[] clusterMapIndices, uint characterIndex, CanvasGlyphOrientation glyphOrientation)
    {
        foreach (var range in _ranges)
        {
            if (characterIndex >= range.Start && characterIndex < range.End)
            {
                using var solid = new CanvasSolidColorBrush(_session, _all ?? range.Color);
                _session.DrawGlyphRun(point + new Vector2(0, range.Shift), fontFace, fontSize, glyphs, isSideways, bidiLevel, solid, measuringMode);
                return;
            }
        }

        if (_all is { } color)
        {
            using var solid = new CanvasSolidColorBrush(_session, color);
            _session.DrawGlyphRun(point, fontFace, fontSize, glyphs, isSideways, bidiLevel, solid, measuringMode);
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
