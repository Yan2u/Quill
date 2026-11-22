using System.Numerics;
using System.Text;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Windows.Foundation;
using Windows.UI;

namespace Quill.Playground.TextPrototype;

/// <summary>
/// The self-drawn text of a block (Q62 prototype): a <see cref="CanvasTextLayout"/> built from styled
/// runs, and everything the editor needs from it: drawing (backgrounds, selection, text, shifted
/// scripts, composition underline), caret rectangles, hit testing and range regions. Independent of
/// XAML, so it can lay out blocks outside the element tree (Q64) and run without a window.
/// </summary>
internal sealed class StyledTextLayout : IDisposable
{
    private readonly TextTheme _theme;
    private readonly List<(int Start, int Length, ProtoStyle Style)> _spans = [];
    private readonly List<BoxInlineObject> _objects = [];
    private readonly List<(int Start, int End, float Shift, Color Color)> _shifted = [];

    public StyledTextLayout(ICanvasResourceCreator creator, IReadOnlyList<ProtoRun> runs, TextTheme theme, float width)
    {
        _theme = theme;
        var text = new StringBuilder();
        foreach (var run in runs)
        {
            _spans.Add((text.Length, run.Text.Length, run.Style));
            text.Append(run.Text);
        }

        Text = text.ToString();
        using var format = new CanvasTextFormat
        {
            FontFamily = theme.FontFamily,
            FontSize = theme.FontSize,
            WordWrapping = CanvasWordWrapping.Wrap,
            IncrementalTabStop = theme.IncrementalTabStop,
            Options = CanvasDrawTextOptions.EnableColorFont,
        };

        // A paragraph holding only a centered object (an image) is centered (10-elements.md).
        if (runs is [{ Object.Align: ProtoObjectAlign.Center }])
        {
            format.HorizontalAlignment = CanvasHorizontalAlignment.Center;
        }

        var (ascent, descent) = Metrics(creator, format);
        Ascent = ascent;
        Descent = descent;
        Layout = new CanvasTextLayout(creator, Text, format, width, float.MaxValue) { Options = CanvasDrawTextOptions.EnableColorFont };
        int offset = 0;
        foreach (var run in runs)
        {
            Apply(run, offset, ascent, descent);
            offset += run.Text.Length;
        }

        float height = Layout.LineMetrics.Sum(static line => line.Height);
        Size = new Vector2(width, Math.Max(height, (float)Layout.LayoutBounds.Height));
    }

    public string Text { get; }

    public CanvasTextLayout Layout { get; }

    public Vector2 Size { get; }

    /// <summary>The ascent and descent of the base font: the caret covers them around the baseline (05: the caret has the height of its character).</summary>
    public float Descent { get; }

    /// <summary>The ascent of the base font: the baseline of a line without taller content.</summary>
    public float Ascent { get; }

    public void Draw(CanvasDrawingSession session, Vector2 origin, (int Start, int End)? selection = null, (int Start, int End)? composition = null)
    {
        foreach (var (start, length, style) in _spans)
        {
            if (style.HasFlag(ProtoStyle.Code) || style.HasFlag(ProtoStyle.Highlight))
            {
                var color = style.HasFlag(ProtoStyle.Code) ? _theme.CodeBackground : _theme.HighlightBackground;
                foreach (var region in Layout.GetCharacterRegions(start, length))
                {
                    var bounds = Offset(region.LayoutBounds, origin);
                    session.FillRoundedRectangle(new Rect(bounds.X - 2, bounds.Y + 1, bounds.Width + 4, bounds.Height - 2), 3, 3, color);
                }
            }
        }

        if (selection is { } selected && selected.End > selected.Start)
        {
            foreach (var bounds in Regions(selected.Start, selected.End))
            {
                session.FillRectangle(Offset(bounds, origin), _theme.SelectionBackground);
            }
        }

        foreach (var box in _objects)
        {
            box.Session = session;
        }

        session.DrawTextLayout(Layout, origin, _theme.Foreground);
        if (_shifted.Count > 0)
        {
            Layout.DrawToTextRenderer(new ShiftedRunRenderer(session, _shifted), origin);
        }

        foreach (var box in _objects)
        {
            box.Session = null;
        }

        if (composition is { } composing && composing.End > composing.Start)
        {
            foreach (var bounds in Regions(composing.Start, composing.End))
            {
                var rect = Offset(bounds, origin);
                float y = (float)(rect.Y + rect.Height - 2);
                session.DrawLine((float)rect.X, y, (float)(rect.X + rect.Width), y, _theme.CompositionUnderline, 1);
            }
        }
    }

    /// <summary>
    /// The caret at a text offset: with <paramref name="backward"/> on the trailing edge of the
    /// character before it, otherwise on the leading edge of the character after it (03, Affinity).
    /// A caret in a superscript or subscript is shortened and shifted with the script.
    /// </summary>
    public Rect CaretRect(int offset, bool backward)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        bool trailing = backward && offset > 0 || offset == Text.Length && offset > 0;
        int character = trailing ? offset - 1 : offset;
        var position = Layout.GetCaretPosition(character, trailing);
        var (top, baseline, _) = LineOf(character);
        if (ShiftOf(character) is { } shift)
        {
            // Cover the script's own glyph box: its ascent above the shifted baseline, its line height.
            float scale = _theme.ScriptScale;
            return new Rect(position.X, top + baseline + shift.Shift - Ascent * scale, 1, (Ascent + Descent) * scale);
        }

        return new Rect(position.X, top + baseline - Ascent, 1, Ascent + Descent);
    }

    /// <summary>The caret offset nearest to a point.</summary>
    public int HitTest(Vector2 point)
    {
        Layout.HitTest(point.X, point.Y, out var region, out bool trailing);
        return region.CharacterIndex + (trailing ? region.CharacterCount : 0);
    }

    /// <summary>
    /// The rectangles covering a range of the text, one per line. DirectWrite reports one region per
    /// formatting run, so the regions of a line are merged.
    /// </summary>
    public IEnumerable<Rect> Regions(int start, int end)
    {
        var merged = new List<Rect>();
        foreach (var region in Layout.GetCharacterRegions(start, end - start))
        {
            var bounds = region.LayoutBounds;
            int line = LineIndexAt(bounds.Y + bounds.Height / 2);
            int last = merged.Count - 1;
            if (last >= 0 && LineIndexAt(merged[last].Y + merged[last].Height / 2) == line)
            {
                var previous = merged[last];
                double left = Math.Min(previous.X, bounds.X);
                double right = Math.Max(previous.X + previous.Width, bounds.X + bounds.Width);
                double top = Math.Min(previous.Y, bounds.Y);
                double bottom = Math.Max(previous.Y + previous.Height, bounds.Y + bounds.Height);
                merged[last] = new Rect(left, top, right - left, bottom - top);
            }
            else
            {
                merged.Add(bounds);
            }
        }

        return merged;
    }

    /// <summary>The top, baseline (from the top) and height of the line holding a character.</summary>
    public (float Top, float Baseline, float Height) LineOf(int character)
    {
        float top = 0;
        int index = 0;
        var lines = Layout.LineMetrics;
        for (int i = 0; i < lines.Length; i++)
        {
            if (character < index + lines[i].CharacterCount || i == lines.Length - 1)
            {
                return (top, lines[i].Baseline, lines[i].Height);
            }

            top += lines[i].Height;
            index += lines[i].CharacterCount;
        }

        return (0, Ascent, Size.Y);
    }

    private int LineIndexAt(double y)
    {
        float top = 0;
        var lines = Layout.LineMetrics;
        for (int i = 0; i < lines.Length; i++)
        {
            top += lines[i].Height;
            if (y < top)
            {
                return i;
            }
        }

        return lines.Length - 1;
    }

    public void Dispose() => Layout.Dispose();

    private static (float Ascent, float Descent) Metrics(ICanvasResourceCreator creator, CanvasTextFormat format)
    {
        using var probe = new CanvasTextLayout(creator, "x", format, 1000, 1000);
        var line = probe.LineMetrics[0];
        return (line.Baseline, line.Height - line.Baseline);
    }

    private static Rect Offset(Rect rect, Vector2 origin) => new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);

    private (int Start, int End, float Shift, Color Color)? ShiftOf(int character)
    {
        foreach (var range in _shifted)
        {
            if (character >= range.Start && character < range.End)
            {
                return range;
            }
        }

        return null;
    }

    private void Apply(ProtoRun run, int start, float ascent, float descent)
    {
        int length = run.Text.Length;
        if (run.Object is { } value)
        {
            var box = new BoxInlineObject(value, ascent, descent);
            _objects.Add(box);
            Layout.SetInlineObject(start, length, box);
            return;
        }

        var style = run.Style;
        if (style.HasFlag(ProtoStyle.Strong))
        {
            Layout.SetFontWeight(start, length, FontWeights.Bold);
        }

        if (style.HasFlag(ProtoStyle.Emphasis))
        {
            Layout.SetFontStyle(start, length, Windows.UI.Text.FontStyle.Italic);
        }

        if (style.HasFlag(ProtoStyle.Strikethrough))
        {
            Layout.SetStrikethrough(start, length, true);
        }

        if (style.HasFlag(ProtoStyle.Code) || style.HasFlag(ProtoStyle.Marker))
        {
            Layout.SetFontFamily(start, length, _theme.MonospaceFamily);
        }

        var color = style.HasFlag(ProtoStyle.Marker) ? _theme.MarkerForeground
            : style.HasFlag(ProtoStyle.Link) ? _theme.LinkForeground
            : _theme.Foreground;
        if (style.HasFlag(ProtoStyle.Superscript) || style.HasFlag(ProtoStyle.Subscript))
        {
            // Smaller, drawn transparent by the main pass and again shifted by ShiftedRunRenderer.
            Layout.SetFontSize(start, length, _theme.FontSize * _theme.ScriptScale);
            float shift = style.HasFlag(ProtoStyle.Superscript) ? -_theme.FontSize * 0.35f : _theme.FontSize * 0.2f;
            _shifted.Add((start, start + length, shift, color));
            color = Color.FromArgb(0, 0, 0, 0);
        }

        if (color != _theme.Foreground)
        {
            Layout.SetColor(start, length, color);
        }
    }
}
