using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A definition block (02, 10): its slots on one row with drawn decorations between them (brackets,
/// colon, quotes), any further blocks below, indented. Every slot but the last takes its natural
/// width; the last takes the rest of the row and wraps. What follows the last slot (a closing
/// quote, footnote backlinks) follows its text, so the slot draws it itself (slots are opaque).
/// </summary>
public abstract partial class MdDefinitionView : MdContainerView
{
    private const double MinimumLastSlot = 80;

    private readonly List<(CanvasTextLayout Label, double X)> _decorations = [];
    private double _rowHeight;
    private double[] _slotX = [];
    private double[] _slotWidths = [];
    private bool _measuring;

    protected MdDefinitionView()
        : base(draws: true)
    {
    }

    public override double FirstBaseline => Blocks.Count > 0 && Blocks[0] is MdBlockView first ? first.FirstBaseline : base.FirstBaseline;

    /// <summary>The number of children that are slots; the rest are blocks below the row.</summary>
    protected abstract int SlotCount { get; }

    /// <summary>The row: decoration strings and slot indexes, left to right.</summary>
    protected abstract IEnumerable<object> RowParts();

    /// <summary>Labels drawn after the text of the last slot, after the row's closing decorations.</summary>
    private protected virtual IEnumerable<TrailingLabel> ExtraTrailing() => [];

    protected override void OnActiveChanged() => InvalidateMeasure();

    protected override void OnContextChanged() => DropDecorations();

    protected override void OnBlockPooled()
    {
        base.OnBlockPooled();
        DropDecorations();
        _slotX = [];
        _slotWidths = [];
        _rowHeight = 0;
    }

    protected override void OnBlockUnmounted()
    {
        base.OnBlockUnmounted();
        DropDecorations();
    }

    internal void InvalidateSlotWidths()
    {
        if (!_measuring)
        {
            InvalidateMeasure();
        }
    }

    protected override Size MeasureBlock(Size availableSize)
    {
        _measuring = true;
        try
        {
            DropDecorations();
            double width = double.IsInfinity(availableSize.Width) ? 100000 : availableSize.Width;
            int slots = Math.Min(SlotCount, Blocks.Count);
            _slotX = new double[slots];
            _slotWidths = new double[slots];
            var parts = RowParts().ToList();
            int lastSlot = parts.OfType<int>().Where(index => index < slots).DefaultIfEmpty(-1).Max();
            var trailing = new List<TrailingLabel>();
            double trailingWidth = parts.SkipWhile(part => !Equals(part, lastSlot)).Skip(1).OfType<string>().Sum(TextWidth);
            double x = 0;
            bool afterLast = false;
            foreach (var part in parts)
            {
                if (part is string text)
                {
                    if (afterLast)
                    {
                        trailing.Add(new TrailingLabel(text, Sheet.SecondaryForeground, 1, 0));
                        continue;
                    }

                    var label = CreateLabel(text, Sheet.FontSize);
                    _decorations.Add((label, x));
                    x += label.LayoutBoundsIncludingTrailingWhitespace.Width;
                }
                else if (part is int index && index < slots && Blocks[index] is MdTextBlockView slot)
                {
                    double natural = slot is MdDefinitionSlot { IsEmpty: true } ? Sheet.FontSize : slot.MeasureNaturalWidth();
                    double slotWidth = index == lastSlot
                        ? Math.Max(MinimumLastSlot, width - x - trailingWidth)
                        : Math.Min(natural, Math.Max(Sheet.FontSize, width - x - MinimumLastSlot));
                    _slotX[index] = x;
                    _slotWidths[index] = slotWidth;
                    x += slotWidth;
                    afterLast = index == lastSlot;
                }
            }

            trailing.AddRange(ExtraTrailing());
            for (int i = 0; i < slots; i++)
            {
                if (Blocks[i] is MdDefinitionSlot slot)
                {
                    slot.Trailing = i == lastSlot ? trailing : [];
                }
            }

            _rowHeight = 0;
            double height = 0;
            for (int i = 0; i < Blocks.Count; i++)
            {
                if (i < slots)
                {
                    Blocks[i].Measure(new Size(_slotWidths[i], double.PositiveInfinity));
                    _rowHeight = Math.Max(_rowHeight, Blocks[i].DesiredSize.Height);
                }
                else
                {
                    Blocks[i].Measure(new Size(Math.Max(0, width - Sheet.ListIndent), double.PositiveInfinity));
                    height += Sheet.BlockSpacing + Blocks[i].DesiredSize.Height;
                }
            }

            return new Size(double.IsInfinity(availableSize.Width) ? x : availableSize.Width, _rowHeight + height);
        }
        finally
        {
            _measuring = false;
        }
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        ResetArrangedChildren();
        int slots = Math.Min(_slotWidths.Length, Blocks.Count);
        for (int i = 0; i < slots; i++)
        {
            ArrangeBlockChild(Blocks[i], new Rect(_slotX[i], 0, _slotWidths[i], Blocks[i].DesiredSize.Height));
        }

        double y = _rowHeight;
        for (int i = slots; i < Blocks.Count; i++)
        {
            y += Sheet.BlockSpacing;
            ArrangeBlockChild(Blocks[i], new Rect(Sheet.ListIndent, y, Math.Max(0, finalSize.Width - Sheet.ListIndent), Blocks[i].DesiredSize.Height));
            y += Blocks[i].DesiredSize.Height;
        }

        if (HasOwnCanvas)
        {
            Redraw();
        }
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        double baseline = FirstBaseline;
        foreach (var (label, x) in _decorations)
        {
            session.DrawTextLayout(label, new Vector2((float)x, (float)baseline - label.LineMetrics[0].Baseline), Sheet.SecondaryForeground);
        }
    }

    private double TextWidth(string text)
    {
        using var label = CreateLabel(text, Sheet.FontSize);
        return label.LayoutBoundsIncludingTrailingWhitespace.Width;
    }

    private void DropDecorations()
    {
        foreach (var (label, _) in _decorations)
        {
            label.Dispose();
        }

        _decorations.Clear();
    }
}
