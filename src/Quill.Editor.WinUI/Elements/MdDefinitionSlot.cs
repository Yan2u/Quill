using System.Numerics;
using Microsoft.Graphics.Canvas;
using Quill.Editor.Documents;
using Quill.Editor.WinUI.Localization;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A slot of a definition block (02, 10): footnote name and description, link label, destination
/// and title. The brackets, colon and quotes between slots are decorations drawn by the
/// definition; what follows the last slot's text (a closing quote, footnote backlinks) is drawn by
/// the slot itself. An empty description shows a placeholder.
/// </summary>
public sealed partial class MdDefinitionSlot : MdInlineTextView
{
    private const float TrailingGap = 2;

    private DefinitionSlotKind _slotKind;
    private IReadOnlyList<TrailingLabel> _trailing = [];

    public DefinitionSlotKind SlotKind
    {
        get => _slotKind;
        set => SetAndRedraw(ref _slotKind, value);
    }

    /// <summary>The slot has no text.</summary>
    public bool IsEmpty => Content.Runs.IsDefaultOrEmpty;

    /// <summary>
    /// The index of the backlink at a point in this control's coordinates, or null: the trailing
    /// label marked as backlinks holds one mark per reference (Q72).
    /// </summary>
    internal int? BacklinkAt(Windows.Foundation.Point point)
    {
        foreach (var (label, bounds) in TrailingBounds())
        {
            if (label.Backlinks > 0 && bounds.Contains(point))
            {
                int index = (int)((point.X - bounds.X) / (bounds.Width / label.Backlinks));
                return Math.Clamp(index, 0, label.Backlinks - 1);
            }
        }

        return null;
    }

    /// <summary>Where each trailing label goes: after the text, or after the placeholder of an empty slot.</summary>
    private IEnumerable<(TrailingLabel Label, Rect Bounds)> TrailingBounds()
    {
        if (TextLayout is not { } layout)
        {
            yield break;
        }

        var padding = TextPadding;
        float x = (float)padding.Left;
        float baseline = (float)padding.Top + layout.FirstBaseline;
        if (IsEmpty)
        {
            if (_slotKind == DefinitionSlotKind.FootnoteContent)
            {
                using var placeholder = CreateLabel(UiStrings.Get("editor.footnotePlaceholder"), Sheet.FontSize);
                x += (float)placeholder.LayoutBounds.Width;
            }
        }
        else
        {
            var end = layout.EndOfText();
            var (top, lineBaseline, _) = layout.LineOf(layout.Text.Length - 1);
            x += (float)end.X;
            baseline = (float)padding.Top + top + lineBaseline;
        }

        x += TrailingGap;
        foreach (var trailing in _trailing)
        {
            using var label = CreateLabel(trailing.Text, Sheet.FontSize * trailing.Scale);
            float y = baseline + trailing.Shift * Sheet.FontSize - label.LineMetrics[0].Baseline;
            float width = (float)label.LayoutBoundsIncludingTrailingWhitespace.Width;
            yield return (trailing, new Rect(x, y, width, label.LineMetrics[0].Height));
            x += width;
        }
    }

    /// <summary>Set by the definition before it measures the slot.</summary>
    internal IReadOnlyList<TrailingLabel> Trailing
    {
        get => _trailing;
        set
        {
            if (!_trailing.SequenceEqual(value))
            {
                _trailing = value;
                Redraw();
            }
        }
    }

    protected override void DrawBackground(CanvasDrawingSession session, Size size)
    {
        if (TextLayout is not { } layout)
        {
            return;
        }

        if (IsEmpty && _slotKind == DefinitionSlotKind.FootnoteContent)
        {
            var padding = TextPadding;
            float baseline = (float)padding.Top + layout.FirstBaseline;
            using var placeholder = CreateLabel(UiStrings.Get("editor.footnotePlaceholder"), Sheet.FontSize);
            session.DrawTextLayout(placeholder, new Vector2((float)padding.Left, baseline - placeholder.LineMetrics[0].Baseline), Sheet.PlaceholderForeground);
        }

        foreach (var (trailing, bounds) in TrailingBounds())
        {
            using var label = CreateLabel(trailing.Text, Sheet.FontSize * trailing.Scale);
            session.DrawTextLayout(label, new Vector2((float)bounds.X, (float)bounds.Y), trailing.Color);
        }
    }

    protected override void OnNaturalWidthInvalidated() =>
        (ParentOf(this) as MdDefinitionView)?.InvalidateSlotWidths();
}
