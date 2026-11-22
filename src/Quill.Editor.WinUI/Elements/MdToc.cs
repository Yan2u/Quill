using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Geometry;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Localization;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// The table of contents: one line per heading, indented by level, in the link color (Q68); a click
/// on an entry goes to its heading (the box asks <see cref="EntryAt"/>). Under a block caret it
/// is active: it shows its read-only source (<c>[toc]</c>) above the entries and a dashed frame (Q59).
/// </summary>
public sealed partial class MdToc : MdAtomView
{
    private const double Padding = 8;
    private const double LevelIndent = 18;

    private string _source = "[toc]";
    private bool _isActive;
    private TocContent _entries = TocContent.Empty;
    private double _line;

    /// <summary>The source line, shown while active.</summary>
    public string Source
    {
        get => _source;
        set => SetAndRemeasure(ref _source, value ?? "[toc]");
    }

    public bool IsActive
    {
        get => _isActive;
        set => SetAndRemeasure(ref _isActive, value);
    }

    public TocContent Entries
    {
        get => _entries;
        set => SetAndRemeasure(ref _entries, value ?? TocContent.Empty);
    }

    /// <summary>The key of the heading whose entry is at a point in this control's coordinates, or null.</summary>
    internal string? EntryAt(Point point)
    {
        if (_line <= 0)
        {
            return null;
        }

        double top = Padding + (_isActive ? _line : 0);
        int row = (int)Math.Floor((point.Y - top) / _line);
        if (row < 0 || row >= _entries.Entries.Length)
        {
            return null;
        }

        var entry = _entries.Entries[row];
        return point.X >= Padding + LevelIndent * (Math.Clamp(entry.Level, 1, 6) - 1) ? entry.Key : null;
    }

    protected override void OnContextChanged() => _line = 0;

    protected override Size MeasureBlock(Size availableSize)
    {
        if (_line <= 0)
        {
            _line = BodyLineHeight();
        }

        int lines = Math.Max(1, _entries.Entries.Length) + (_isActive ? 1 : 0);
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 2 * Padding + _line * lines);
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        float y = (float)Padding;
        if (_isActive)
        {
            using var source = CreateLabel(_source, Sheet.FontSize * Sheet.CodeScale, monospace: true);
            session.DrawTextLayout(source, new Vector2((float)Padding, y + (float)(_line - source.LineMetrics[0].Height) / 2), Sheet.SecondaryForeground);
            y += (float)_line;
        }

        if (_entries.Entries.IsEmpty)
        {
            using var title = CreateLabel(UiStrings.Get("editor.tocEmpty"), Sheet.FontSize);
            session.DrawTextLayout(title, new Vector2((float)Padding, y + (float)(_line - title.LineMetrics[0].Height) / 2), Sheet.PlaceholderForeground);
        }

        foreach (var entry in _entries.Entries)
        {
            using var label = CreateLabel(entry.Text.Length > 0 ? entry.Text : " ", Sheet.FontSize);
            float x = (float)(Padding + LevelIndent * (Math.Clamp(entry.Level, 1, 6) - 1));
            session.DrawTextLayout(label, new Vector2(x, y + (float)(_line - label.LineMetrics[0].Height) / 2), Sheet.LinkForeground);
            y += (float)_line;
        }

        if (HasBlockCaret && ActualWidth > 2 && ActualHeight > 2)
        {
            using var dashed = new CanvasStrokeStyle { DashStyle = CanvasDashStyle.Dash };
            session.DrawRectangle(new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), Sheet.BlockCaret, 1, dashed);
        }
    }
}
