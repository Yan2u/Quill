using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Text;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A heading of level 1 to 6. The <c>H1</c>…<c>H6</c> badge of the focused heading is an overlay
/// that never moves the text (10-elements.md). The first levels have a rule below (style sheet:
/// <c>HeadingRule</c>, github: h1 and h2).
/// </summary>
public sealed partial class MdHeading : MdInlineTextView
{
    private int _level = 1;

    public int Level
    {
        get => _level;
        set
        {
            if (_level != value)
            {
                _level = value;
                InvalidateMeasure();
                InvalidateText();
            }
        }
    }

    private protected override BlockTextFormat Format()
    {
        // Inside a container that recolors its text (a quote), the heading keeps that color.
        var foreground = Context.Foreground != Sheet.Foreground ? Context.Foreground
            : _level >= 6 ? Sheet.Heading6Foreground
            : Sheet.HeadingForeground;
        return new BlockTextFormat(Sheet.FontFamily, Sheet.HeadingSize(_level), foreground, Bold: true, LineHeight: Sheet.HeadingLineHeight);
    }

    private bool HasRule => _level <= Sheet.HeadingRuleLevels && Sheet.HeadingRuleThickness > 0;

    protected override Thickness Padding() => HasRule ? new Thickness(0, 0, 0, Sheet.HeadingRuleGap + Sheet.HeadingRuleThickness) : default;

    protected override void DrawBackground(CanvasDrawingSession session, Size size)
    {
        if (HasRule)
        {
            session.FillRectangle(new Rect(0, size.Height - Sheet.HeadingRuleThickness, size.Width, Sheet.HeadingRuleThickness), Sheet.HeadingRule);
        }
    }
}
