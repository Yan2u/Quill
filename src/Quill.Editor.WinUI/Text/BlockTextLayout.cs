using System.Collections.Concurrent;
using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Text;
using Quill.Editor.WinUI.Rendering.Formulas;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;
using Windows.UI;

namespace Quill.Editor.WinUI.Text;

/// <summary>
/// The self-drawn text of a block at one width (Q62, from the Q8 prototype): a
/// <see cref="CanvasTextLayout"/> built from a <see cref="TextSource"/>, plus everything the editor
/// needs from it: drawing (span backgrounds, selection, text, shifted scripts, composition
/// underline), caret rectangles, hit testing and range regions. Independent of XAML, so blocks can
/// be laid out outside the element tree (Q64) and in tests without a window.
/// </summary>
internal sealed class BlockTextLayout : ITextBlockLayout
{
    private static readonly ConcurrentDictionary<(string Family, float FontSize), (float Above, float Below)> InkMetricCache = new();

    private readonly TextSource _source;
    private readonly MdStyleSheet _sheet;
    private readonly BlockTextFormat _format;
    private readonly List<InlineBox> _boxes = [];
    private readonly List<CanvasTextLayout> _ownedLayouts = [];
    private readonly List<ShiftedRange> _shifted = [];
    private readonly float _maxObjectWidth;

    /// <summary>The proportional line spacing of lines with objects, which images are exempt from (see <see cref="InlineBox"/>).</summary>
    private readonly float _leading;

    /// <summary>Where decoded images were last drawn, in the coordinates the layout was drawn in, for tests.</summary>
    internal List<Rect> DrawnImages { get; } = [];

    // The line metrics with the offset and top of each line: Win2D returns a new array on every
    // access, and lines are looked up by index, offset and position all the time.
    private CanvasLineMetrics[]? _lines;
    private int[]? _lineStarts;
    private float[]? _lineTops;

    public BlockTextLayout(ICanvasResourceCreator creator, TextSource source, BlockTextFormat format, MdStyleSheet sheet, float width)
    {
        _source = source;
        _sheet = sheet;
        _format = format;
        _maxObjectWidth = Math.Max(1, width);
        using var textFormat = CreateFormat(format, sheet, spacing: false);
        (Ascent, Descent) = Metrics(creator, textFormat);
        using var spacedFormat = CreateFormat(format, sheet, spacing: true);
        float emptyBaseline;
        using (var probe = new CanvasTextLayout(creator, "x", spacedFormat, 1000, 1000))
        {
            EmptyLineHeight = probe.LineMetrics[0].Height;
            emptyBaseline = probe.LineMetrics[0].Baseline;
        }

        // Every line as tall as a line of the block's font, its baseline at the same place, as CSS line-height
        // does: otherwise a line's height follows the fonts on it, and the monospace markers that appear when the
        // caret enters (`**`, backticks) or fallback fonts change it, moving the text. Inline objects (images,
        // formulas) can be taller than a line, so blocks with them keep the line height of their content.
        if (source.Objects.Length == 0)
        {
            emptyBaseline = Ascent + (EmptyLineHeight - (Ascent + Descent)) / 2f;
            spacedFormat.LineSpacingMode = CanvasLineSpacingMode.Uniform;
            spacedFormat.LineSpacing = EmptyLineHeight;
            spacedFormat.LineSpacingBaseline = emptyBaseline;
        }

        EmptyBaseline = emptyBaseline;

        _leading = spacedFormat.LineSpacingMode == CanvasLineSpacingMode.Proportional ? spacedFormat.LineSpacing : 1;

        Layout = new CanvasTextLayout(creator, source.Text, spacedFormat, Math.Max(1, width), float.MaxValue)
        {
            Options = CanvasDrawTextOptions.EnableColorFont,
        };
        foreach (var span in source.Spans)
        {
            ApplySpan(span);
        }

        foreach (var value in source.Objects)
        {
            var box = CreateBox(creator, value);
            _boxes.Add(box);
            Layout.SetInlineObject(value.Index, 1, box);
        }

        float height = source.Text.Length == 0 ? EmptyLineHeight : Lines.Sum(static line => line.Height);
        Height = Math.Max(height, EmptyLineHeight);
        Width = width;
    }

    public CanvasTextLayout Layout { get; }

    public string Text => _source.Text;

    public float Width { get; }

    public float Height { get; }

    /// <summary>The ascent of the base font: carets cover ascent and descent around the baseline (05).</summary>
    public float Ascent { get; }

    public float Descent { get; }

    /// <summary>The height of one line, also the height of an empty block.</summary>
    public float EmptyLineHeight { get; }

    private float EmptyBaseline { get; }

    /// <summary>The baseline of the first line from the top: list markers align with it.</summary>
    public float FirstBaseline => _source.Text.Length == 0 ? EmptyBaseline : Lines[0].Baseline;

    /// <summary>The width of the widest line.</summary>
    public float ContentWidth => (float)Layout.LayoutBounds.Width;

    /// <summary>The right end of the last line: where decorations after the text go (footnote backlinks).</summary>
    public Point EndOfText()
    {
        var caret = CaretRect(_source.Text.Length, backward: true);
        return new Point(caret.X, caret.Y);
    }

    public void Draw(CanvasDrawingSession session, Vector2 origin, (int Start, int End)? selection = null, (int Start, int End)? composition = null, Rect? visible = null, IReadOnlyList<TextMark>? marks = null)
    {
        DrawSpanBackgrounds(session, origin);
        if (marks is not null)
        {
            foreach (var mark in marks)
            {
                foreach (var bounds in Regions(mark.Start, mark.End))
                {
                    session.FillRectangle(Offset(bounds, origin), mark.Current ? _sheet.SearchCurrentBackground : _sheet.SearchMatchBackground);
                }
            }
        }

        if (selection is { } selected && selected.End > selected.Start)
        {
            foreach (var bounds in Regions(selected.Start, selected.End))
            {
                session.FillRectangle(Offset(bounds, origin), _sheet.SelectionBackground);
            }
        }

        foreach (var box in _boxes)
        {
            box.Session = session;
        }

        session.DrawTextLayout(Layout, origin, _format.Foreground);
        if (_shifted.Count > 0)
        {
            Layout.DrawToTextRenderer(new ShiftedRunRenderer(session, _shifted), origin);
        }

        if (_sheet.SelectionForeground is { } selectedColor && selection is { } recolored && recolored.End > recolored.Start)
        {
            // The selected text again in the selection color, clipped to the selection.
            var rects = Regions(recolored.Start, recolored.End).Select(bounds => Offset(bounds, origin)).ToList();
            if (rects.Count > 0)
            {
                var rectGeometries = new CanvasGeometry[rects.Count];
                try
                {
                    for (int i = 0; i < rects.Count; i++)
                    {
                        rectGeometries[i] = CanvasGeometry.CreateRectangle(session, rects[i]);
                    }

                    using var clip = CanvasGeometry.CreateGroup(session, rectGeometries);
                    using (session.CreateLayer(1, clip))
                    {
                        Layout.DrawToTextRenderer(new ShiftedRunRenderer(session, _shifted, selectedColor), origin);
                    }
                }
                finally
                {
                    foreach (CanvasGeometry rectGeometry in rectGeometries)
                    {
                        rectGeometry?.Dispose();
                    }
                }
            }
        }

        foreach (var box in _boxes)
        {
            box.Session = null;
        }

        if (composition is { } composing && composing.End > composing.Start)
        {
            foreach (var bounds in Regions(composing.Start, composing.End))
            {
                var rect = Offset(bounds, origin);
                float y = (float)(rect.Y + rect.Height - 2);
                session.DrawLine((float)rect.X, y, (float)(rect.X + rect.Width), y, _sheet.CompositionUnderline, 1);
            }
        }
    }

    /// <summary>
    /// The caret at a display offset: with <paramref name="backward"/> on the trailing edge of the
    /// character before it, otherwise on the leading edge of the character after it (03, Affinity).
    /// A caret in a superscript or subscript is shortened and shifted with the script.
    /// </summary>
    public Rect CaretRect(int offset, bool backward)
    {
        offset = Math.Clamp(offset, 0, _source.Text.Length);
        if (_source.Text.Length == 0)
        {
            float x = _format.Alignment switch
            {
                CanvasHorizontalAlignment.Center => Width / 2,
                CanvasHorizontalAlignment.Right => Width,
                _ => 0,
            };
            return new Rect(x, EmptyBaseline - Ascent, 1, Ascent + Descent);
        }

        bool trailing = Trailing(offset, backward);
        int character = trailing ? offset - 1 : offset;
        var position = Layout.GetCaretPosition(character, trailing);
        var (top, baseline, _) = LineOf(character);
        foreach (var range in _shifted)
        {
            if (character >= range.Start && character < range.End)
            {
                float scale = _sheet.ScriptScale;
                return new Rect(position.X, top + baseline + range.Shift - Ascent * scale, 1, (Ascent + Descent) * scale);
            }
        }

        return new Rect(position.X, top + baseline - Ascent, 1, Ascent + Descent);
    }

    /// <summary>The number of visual lines (an empty block has one).</summary>
    public int LineCount => _source.Text.Length == 0 ? 1 : Lines.Length;

    /// <summary>
    /// A visual line: its first and last caret offsets (the end before a line break character) and
    /// its vertical extent.
    /// </summary>
    public (int Start, int End, float Top, float Height) Line(int index)
    {
        if (_source.Text.Length == 0)
        {
            return (0, 0, 0, EmptyLineHeight);
        }

        var lines = Lines;
        index = Math.Clamp(index, 0, lines.Length - 1);
        int start = _lineStarts![index];
        float top = _lineTops![index];
        int end = start + lines[index].CharacterCount;
        if (end > start && _source.Text[end - 1] == '\n')
        {
            end--;
        }

        return (start, Math.Min(end, _source.Text.Length), top, lines[index].Height);
    }

    /// <summary>The visual line of a caret, with the same affinity rule as <see cref="CaretRect"/>.</summary>
    public int LineIndexOfCaret(int offset, bool backward)
    {
        if (_source.Text.Length == 0)
        {
            return 0;
        }

        offset = Math.Clamp(offset, 0, _source.Text.Length);
        bool trailing = Trailing(offset, backward);
        int character = trailing ? offset - 1 : offset;
        return LineIndexOfCharacter(character);
    }

    /// <summary>
    /// The caret offset nearest to a point, and whether it is the end of a wrapped line: the same
    /// offset is also the start of the next line, so it needs the backward affinity (03).
    /// </summary>
    public (int Offset, bool AtWrappedLineEnd) HitTestLine(Vector2 point)
    {
        if (_source.Text.Length == 0)
        {
            return (0, false);
        }

        int index = LineIndexAt(point.Y);
        var line = Line(index);
        Layout.HitTest(point.X, Math.Clamp(point.Y, line.Top, line.Top + line.Height - 0.01f), out var region, out bool trailing);
        int offset = Math.Clamp(region.CharacterIndex + (trailing ? region.CharacterCount : 0), line.Start, line.End);
        bool wrapped = index < LineCount - 1 && offset == line.End && Line(index + 1).Start == line.End;
        return (offset, wrapped);
    }

    /// <summary>The caret offset nearest to a point.</summary>
    public int HitTest(Vector2 point) => HitTestLine(point).Offset;

    /// <summary>The rectangles covering a range of the text, one per line (DirectWrite reports one per format run).</summary>
    public IReadOnlyList<Rect> Regions(int start, int end)
    {
        var merged = new List<Rect>();
        start = Math.Clamp(start, 0, _source.Text.Length);
        end = Math.Clamp(end, start, _source.Text.Length);
        if (end == start)
        {
            return merged;
        }

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
        if (Lines.Length == 0)
        {
            return (0, EmptyBaseline, EmptyLineHeight);
        }

        int index = LineIndexOfCharacter(character);
        return (_lineTops![index], Lines[index].Baseline, Lines[index].Height);
    }

    public void Dispose()
    {
        Layout.Dispose();
        foreach (var layout in _ownedLayouts)
        {
            layout.Dispose();
        }
    }

    internal static CanvasTextFormat CreateFormat(BlockTextFormat format, MdStyleSheet sheet, bool spacing)
    {
        var textFormat = new CanvasTextFormat
        {
            FontFamily = format.Monospace ? sheet.MonospaceFamily : format.FontFamily,
            FontSize = format.FontSize,
            FontWeight = format.Bold ? new Windows.UI.Text.FontWeight { Weight = sheet.BlockBoldWeight } : FontWeights.Normal,
            WordWrapping = format.Wrap ? CanvasWordWrapping.Wrap : CanvasWordWrapping.NoWrap,
            IncrementalTabStop = sheet.IncrementalTabStop,
            HorizontalAlignment = format.Alignment,
            Options = CanvasDrawTextOptions.EnableColorFont,
        };
        if (spacing)
        {
            // Proportional spacing scales line height and baseline alike: the extra space splits by ascent and descent.
            textFormat.LineSpacingMode = CanvasLineSpacingMode.Proportional;
            float factor = MdStyleSheet.LineSpacingFor(textFormat.FontFamily, format.LineHeight > 0 ? format.LineHeight : sheet.LineHeight);
            textFormat.LineSpacing = factor;
            textFormat.LineSpacingBaseline = factor;
        }

        return textFormat;
    }

    /// <summary>
    /// Whether a caret sits on the trailing edge of the character before it: with backward affinity
    /// and at the end of the text, except after a line break, which has no trailing edge to stand on.
    /// </summary>
    private bool Trailing(int offset, bool backward) =>
        offset > 0 && (backward || offset == _source.Text.Length) && _source.Text[offset - 1] != '\n';

    private static (float Ascent, float Descent) Metrics(ICanvasResourceCreator creator, CanvasTextFormat format)
    {
        using var probe = new CanvasTextLayout(creator, "x", format, 1000, 1000);
        var line = probe.LineMetrics[0];
        return (line.Baseline, line.Height - line.Baseline);
    }

    private static Rect Offset(Rect rect, Vector2 origin) => new(rect.X + origin.X, rect.Y + origin.Y, rect.Width, rect.Height);

    private void ApplySpan(TextSpan span)
    {
        var (start, length, style) = span;
        if (style.Bold)
        {
            Layout.SetFontWeight(start, length, FontWeights.Bold);
        }

        if (style.Italic)
        {
            Layout.SetFontStyle(start, length, Windows.UI.Text.FontStyle.Italic);
        }

        if (style.Strikethrough)
        {
            Layout.SetStrikethrough(start, length, true);
        }

        if (style.Underline)
        {
            Layout.SetUnderline(start, length, true);
        }

        if (style.Monospace && !_format.Monospace)
        {
            Layout.SetFontFamily(start, length, _sheet.MonospaceFamily);
        }

        float size = _format.FontSize * style.Scale;
        Color color = style.Foreground ?? _format.Foreground;
        if (style.Script != ScriptShift.None)
        {
            // Smaller, drawn transparent by the main pass and again shifted by ShiftedRunRenderer.
            size *= _sheet.ScriptScale;
            float shift = style.Script == ScriptShift.Superscript ? -_format.FontSize * 0.35f : _format.FontSize * 0.2f;
            _shifted.Add(new ShiftedRange(start, start + length, shift, color));
            color = Color.FromArgb(0, 0, 0, 0);
        }

        if (Math.Abs(size - _format.FontSize) > 0.01f)
        {
            Layout.SetFontSize(start, length, size);
        }

        if (color != _format.Foreground)
        {
            Layout.SetColor(start, length, color);
        }
    }

    private void DrawSpanBackgrounds(CanvasDrawingSession session, Vector2 origin)
    {
        foreach (var span in _source.Spans)
        {
            if (span.Style.Background == SpanBackground.None)
            {
                continue;
            }

            var fill = span.Style.Background switch
            {
                SpanBackground.Highlight => _sheet.HighlightBackground,
                SpanBackground.Keyboard => _sheet.KeyboardBackground,
                SpanBackground.Footnote => _sheet.FootnoteBackground,
                _ => _sheet.CodeBackground,
            };
            foreach (var region in Regions(span.Start, span.Start + span.Length))
            {
                var rect = SpanBoxRect(span, region, origin);
                float radius = span.Style.Background is SpanBackground.Code or SpanBackground.Keyboard or SpanBackground.Highlight ? _sheet.InlineCornerRadius : 3;
                session.FillRoundedRectangle(rect, radius, radius, fill);
                var border = span.Style.Background switch
                {
                    SpanBackground.Keyboard => _sheet.KeyboardBorder,
                    SpanBackground.Code => _sheet.CodeBorder,
                    _ => Microsoft.UI.Colors.Transparent,
                };
                if (border.A > 0)
                {
                    session.DrawRoundedRectangle(rect, radius, radius, border, 1);
                }
            }
        }
    }

    internal Rect SpanBoxRect(TextSpan span, Rect region, Vector2 origin)
    {
        var bounds = Offset(region, origin);
        if (span.Style.Script != ScriptShift.None)
        {
            // A shifted span (footnote reference) gets a box around the small glyphs where they are drawn.
            var caret = CaretRect(span.Start, backward: false);
            return new Rect(bounds.X - 1, caret.Y + origin.Y - 1, bounds.Width + 2, caret.Height + 2);
        }

        Microsoft.UI.Xaml.Thickness padding = span.Style.Background switch
        {
            SpanBackground.Code => _sheet.CodePadding,
            SpanBackground.Keyboard => new(2, 2, 2, 2),
            SpanBackground.Highlight => new(1, 2, 1, 2),
            _ => new(2, 1, 2, 1),
        };
        var (above, below) = SpanInkMetrics(span);
        float baselineY = origin.Y + BaselineAtRegion(region);
        double top = baselineY - above - padding.Top;
        double bottom = baselineY + below + padding.Bottom;
        return new Rect(bounds.X - padding.Left, top, bounds.Width + padding.Left + padding.Right, Math.Max(0, bottom - top));
    }

    internal (Rect Box, float BaselineY, float InkAbove, float InkBelow)? FirstCodeBox(Vector2 origin = default)
    {
        foreach (var span in _source.Spans)
        {
            if (span.Style.Background != SpanBackground.Code || span.Style.Script != ScriptShift.None)
            {
                continue;
            }

            foreach (var region in Regions(span.Start, span.Start + span.Length))
            {
                var (above, below) = SpanInkMetrics(span);
                float baselineY = origin.Y + BaselineAtRegion(region);
                return (SpanBoxRect(span, region, origin), baselineY, above, below);
            }
        }

        return null;
    }

    private float BaselineAtRegion(Rect region)
    {
        var lines = Lines;
        if (lines.Length == 0)
        {
            return EmptyBaseline;
        }

        int line = LineIndexAt(region.Y + region.Height / 2);
        return _lineTops![line] + lines[line].Baseline;
    }

    private (float Above, float Below) SpanInkMetrics(TextSpan span)
    {
        string family = span.Style.Monospace || _format.Monospace ? _sheet.MonospaceFamily : _format.FontFamily;
        float fontSize = _format.FontSize * span.Style.Scale;
        return InkMetrics(Layout.Device, family, fontSize);
    }

    private static (float Above, float Below) InkMetrics(ICanvasResourceCreator creator, string family, float fontSize) =>
        InkMetricCache.GetOrAdd((family, fontSize), key =>
        {
            using var format = new CanvasTextFormat
            {
                FontFamily = key.Family,
                FontSize = key.FontSize,
                WordWrapping = CanvasWordWrapping.NoWrap,
            };
            using var probe = new CanvasTextLayout(creator, "Hblgpqy", format, 1000, 1000);
            if (probe.LineMetrics.Length > 0 && probe.DrawBounds is { Height: > 0 } bounds)
            {
                float baseline = probe.LineMetrics[0].Baseline;
                float above = Math.Max(1f, baseline - (float)bounds.Y);
                float below = Math.Max(0f, (float)(bounds.Y + bounds.Height) - baseline);
                return (above, below);
            }

            return (key.FontSize * 0.75f, key.FontSize * 0.25f);
        });

    private InlineBox CreateBox(ICanvasResourceCreator creator, TextObject value)
    {
        if (value.Kind == TextObjectKind.Image && value.Resource is CanvasBitmap bitmap)
        {
            // A decoded image at its size, scaled down to fill the column when wider (10, Typora); among text
            // it is centered on the text, alone in its paragraph it takes the whole column and is centered in it.
            var size = bitmap.Size;
            double scale = Math.Min(1, _maxObjectWidth / Math.Max(1, size.Width));
            float width = (float)(size.Width * scale);
            float height = (float)(size.Height * scale);
            if (_source.IsImageOnly)
            {
                // A hair less than the column, or the box would wrap onto a line of its own after a rounding.
                float column = Math.Max(width, _maxObjectWidth - 0.5f);
                return new InlineBox(column, height, InlineBoxAlignment.Center, 0, Ascent, Descent, (session, rect) =>
                    DrawImage(session, bitmap, new Rect(rect.X + (rect.Width - width) / 2, rect.Y, width, height)), _leading);
            }

            return new InlineBox(width, height, InlineBoxAlignment.Center, 0, Ascent, Descent, (session, rect) => DrawImage(session, bitmap, rect), _leading);
        }

        if (value.Kind == TextObjectKind.Math && MathTypesetter.Typeset(value.Text, display: false) is { } formula)
        {
            // Typeset inline math (M6.3), its baseline on the text baseline; in the text color.
            float size = _format.FontSize * _sheet.MathScale;
            float formulaBaseline = (float)formula.Height * size;
            string family = _format.FontFamily;
            var color = _format.Foreground;
            return new InlineBox(Math.Max(1, (float)formula.Width * size), Math.Max(1, (float)formula.TotalHeight * size), InlineBoxAlignment.Baseline, formulaBaseline, Ascent, Descent, (session, rect) =>
                MathPainter.Draw(session, formula, new Vector2((float)rect.X, (float)rect.Y), size, color, family));
        }

        const float padding = 3;
        bool image = value.Kind == TextObjectKind.Image;
        string text = image ? $"\U0001F5BC {(value.Text.Length > 0 ? value.Text : Localization.UiStrings.Get("editor.imagePlaceholder"))}" : value.Text;
        using var format = CreateFormat(_format with { Monospace = value.Kind == TextObjectKind.Math, Alignment = CanvasHorizontalAlignment.Left, Wrap = false }, _sheet, spacing: false);
        if (image)
        {
            format.FontSize = _format.FontSize * 0.85f;
        }

        var inner = new CanvasTextLayout(creator, text, format, 10000, 10000) { Options = CanvasDrawTextOptions.EnableColorFont };
        _ownedLayouts.Add(inner);
        float textWidth = (float)inner.LayoutBounds.Width;
        float textHeight = inner.LineMetrics.Length > 0 ? inner.LineMetrics[0].Height : Ascent + Descent;
        float baseline = inner.LineMetrics.Length > 0 ? inner.LineMetrics[0].Baseline : Ascent;
        Color foreground = value.Kind switch
        {
            TextObjectKind.Math => _sheet.MathForeground,
            TextObjectKind.Image => _sheet.SecondaryForeground,
            _ => value.Style.Foreground ?? _format.Foreground,
        };
        switch (value.Kind)
        {
            case TextObjectKind.Image:
            {
                // Not decoded yet (M5.5): a framed placeholder with the alt text, centered on the text (10).
                float height = Math.Max(textHeight + 2 * padding, Ascent + Descent);
                float width = textWidth + 4 * padding;
                return new InlineBox(width, height, InlineBoxAlignment.Center, 0, Ascent, Descent, (session, rect) =>
                {
                    session.FillRoundedRectangle(rect, 3, 3, _sheet.ObjectBackground);
                    session.DrawRoundedRectangle(rect, 3, 3, _sheet.ObjectBorder, 1);
                    session.DrawTextLayout(inner, (float)rect.X + 2 * padding, (float)(rect.Y + (rect.Height - textHeight) / 2), foreground);
                });
            }

            case TextObjectKind.Math:
                // The engine is a later topic (Q61): the TeX source in a monospace, math-colored box, on the baseline.
                return new InlineBox(textWidth + 2 * padding, textHeight, InlineBoxAlignment.Baseline, baseline, Ascent, Descent, (session, rect) =>
                {
                    session.FillRoundedRectangle(rect, 3, 3, _sheet.CodeBackground);
                    session.DrawTextLayout(inner, (float)rect.X + padding, (float)rect.Y, foreground);
                });
            default:
                return new InlineBox(textWidth, textHeight, InlineBoxAlignment.Baseline, baseline, Ascent, Descent, (session, rect) =>
                    session.DrawTextLayout(inner, (float)rect.X, (float)rect.Y, foreground));
        }
    }

    private void DrawImage(CanvasDrawingSession session, CanvasBitmap bitmap, Rect rect)
    {
        session.DrawImage(bitmap, rect);
        if (DrawnImages.Count >= 16)
        {
            DrawnImages.RemoveAt(0);
        }

        DrawnImages.Add(rect);
    }

    public int LineIndexAt(double y)
    {
        if (_source.Text.Length == 0)
        {
            return 0;
        }

        // The last line whose top is at or above y.
        var tops = _lineTops ?? Prepare().Tops;
        int low = 0, high = tops.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (tops[middle] <= y)
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

    private CanvasLineMetrics[] Lines => _lines ?? Prepare().Lines;

    private (CanvasLineMetrics[] Lines, float[] Tops) Prepare()
    {
        var lines = Layout.LineMetrics;
        var starts = new int[lines.Length];
        var tops = new float[lines.Length];
        int start = 0;
        float top = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            starts[i] = start;
            tops[i] = top;
            start += lines[i].CharacterCount;
            top += lines[i].Height;
        }

        _lineStarts = starts;
        _lineTops = tops;
        _lines = lines;
        return (lines, tops);
    }

    /// <summary>The line holding a character; characters past the end belong to the last line.</summary>
    private int LineIndexOfCharacter(int character)
    {
        if (_lineStarts is null)
        {
            Prepare();
        }

        var starts = _lineStarts!;
        int low = 0, high = starts.Length - 1;
        while (low < high)
        {
            int middle = (low + high + 1) / 2;
            if (starts[middle] <= character)
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
}
