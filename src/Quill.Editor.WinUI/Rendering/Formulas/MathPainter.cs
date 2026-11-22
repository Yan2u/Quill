using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Brushes;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using RaTeX;
using Windows.UI;

namespace Quill.Editor.WinUI.Rendering.Formulas;

/// <summary>
/// Draws a RaTeX display list with Win2D: glyphs as glyph runs of the KaTeX fonts, rules and
/// rectangles as fills, paths as geometry. Geometry is in em, y down, the baseline at
/// <see cref="DisplayList.Height"/>. Items in the engine's default color (black) take the theme's
/// foreground, so a theme change needs no new layout; colors from <c>\color</c> stay.
/// </summary>
internal static class MathPainter
{
    /// <summary>The number of glyphs drawn with the text font because the KaTeX fonts lack them, for tests.</summary>
    public static int FallbackGlyphs { get; private set; }

    /// <param name="origin">The top left corner of the formula's box.</param>
    /// <param name="fallbackFamily">The text font for characters the KaTeX fonts do not have (CJK in <c>\text</c>).</param>
    public static void Draw(CanvasDrawingSession session, DisplayList layout, Vector2 origin, float fontSize, Color foreground, string fallbackFamily)
    {
        using var brush = new CanvasSolidColorBrush(session, foreground);
        foreach (var item in layout.Items)
        {
            var at = origin + new Vector2((float)item.X * fontSize, (float)item.Y * fontSize);
            brush.Color = ColorOf(item.Color, foreground);
            switch (item)
            {
                case GlyphPathItem glyph:
                    DrawGlyph(session, glyph, at, fontSize * (float)glyph.Scale, brush, fallbackFamily);
                    break;
                case LineItem line:
                {
                    float thickness = Math.Max(1, (float)line.Thickness * fontSize);
                    var end = at + new Vector2((float)line.Width * fontSize, 0);
                    if (line.Dashed)
                    {
                        using var style = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
                        session.DrawLine(at, end, brush, thickness, style);
                    }
                    else
                    {
                        session.DrawLine(at, end, brush, thickness);
                    }

                    break;
                }

                case RectItem rect:
                {
                    float width = (float)rect.Width * fontSize;
                    float height = (float)rect.Height * fontSize;
                    session.FillRectangle(Math.Min(at.X, at.X + width), Math.Min(at.Y, at.Y + height), Math.Abs(width), Math.Abs(height), brush);
                    break;
                }

                case PathItem path:
                {
                    using var geometry = BuildPath(session, path, at, fontSize);
                    if (path.Fill)
                    {
                        session.FillGeometry(geometry, brush);
                    }
                    else
                    {
                        session.DrawGeometry(geometry, brush, Math.Max(1, fontSize * 0.04f));
                    }

                    break;
                }
            }
        }
    }

    private static void DrawGlyph(CanvasDrawingSession session, GlyphPathItem glyph, Vector2 baseline, float size, CanvasSolidColorBrush brush, string fallbackFamily)
    {
        if (KatexFonts.Face(glyph.Font) is { } face)
        {
            int index = face.GetGlyphIndices([glyph.CodePoint])[0];
            if (index != 0)
            {
                session.DrawGlyphRun(baseline, face, size, [new CanvasGlyph { Index = index }], false, 0, brush);
                return;
            }
        }

        // Not in the math fonts: the text font, its baseline on the glyph's baseline.
        FallbackGlyphs++;
        string text = char.ConvertFromUtf32((int)glyph.CodePoint);
        using var format = new CanvasTextFormat { FontFamily = fallbackFamily, FontSize = size, WordWrapping = CanvasWordWrapping.NoWrap };
        using var layout = new CanvasTextLayout(session, text, format, 0, 0);
        float ascent = layout.LineMetrics.Length > 0 ? layout.LineMetrics[0].Baseline : size;
        session.DrawTextLayout(layout, baseline.X, baseline.Y - ascent, brush.Color);
    }

    private static CanvasGeometry BuildPath(ICanvasResourceCreator creator, PathItem path, Vector2 origin, float scale)
    {
        using var builder = new CanvasPathBuilder(creator);
        bool open = false;
        Vector2 Point(double x, double y) => origin + new Vector2((float)x * scale, (float)y * scale);
        foreach (var command in path.Commands)
        {
            switch (command)
            {
                case MoveTo move:
                    if (open)
                    {
                        builder.EndFigure(CanvasFigureLoop.Open);
                    }

                    builder.BeginFigure(Point(move.X, move.Y));
                    open = true;
                    break;
                case LineTo line:
                    builder.AddLine(Point(line.X, line.Y));
                    break;
                case CubicTo cubic:
                    builder.AddCubicBezier(Point(cubic.X1, cubic.Y1), Point(cubic.X2, cubic.Y2), Point(cubic.X, cubic.Y));
                    break;
                case QuadTo quad:
                    builder.AddQuadraticBezier(Point(quad.X1, quad.Y1), Point(quad.X, quad.Y));
                    break;
                case Close:
                    if (open)
                    {
                        builder.EndFigure(CanvasFigureLoop.Closed);
                        open = false;
                    }

                    break;
            }
        }

        if (open)
        {
            builder.EndFigure(CanvasFigureLoop.Open);
        }

        return CanvasGeometry.CreatePath(builder);
    }

    private static Color ColorOf(RatexColor color, Color foreground) =>
        color is { R: 0, G: 0, B: 0, A: 1 }
            ? foreground
            : Color.FromArgb((byte)(color.A * 255), (byte)(color.R * 255), (byte)(color.G * 255), (byte)(color.B * 255));
}
