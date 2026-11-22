using Microsoft.Graphics.Canvas;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A thematic break: a full-width line (10). It never shows its source; under a block caret it
/// gets a border (Q60, Typora: a blue border).
/// </summary>
public sealed partial class MdThematicBreak : MdAtomView
{
    private double _height;

    public override double FirstBaseline => _height * 0.7;

    protected override void OnContextChanged() => _height = 0;

    protected override Size MeasureBlock(Size availableSize)
    {
        if (_height <= 0)
        {
            _height = BodyLineHeight();
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, _height);
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        float y = (float)(ActualHeight / 2);
        session.DrawLine(0, y, (float)ActualWidth, y, Sheet.Rule, (float)Sheet.RuleThickness);
        if (HasBlockCaret && ActualWidth > 2 && ActualHeight > 2)
        {
            session.DrawRoundedRectangle(new Rect(1, 1, ActualWidth - 2, ActualHeight - 2), 3, 3, Sheet.BlockCaret, 2);
        }
    }
}
