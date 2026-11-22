using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// The virtual paragraph at a block boundary (02, 03): an empty line where the caret stands
/// between non-text blocks. It is not in the document and draws nothing; the caret overlay draws the
/// caret in it (M5.2).
/// </summary>
public sealed partial class MdVirtualParagraph : MdBlockView
{
    /// <summary>Nothing to show when a selection covers it (Q84).</summary>
    protected override bool ShowsWholeSelection => false;

    private double _height;

    public MdVirtualParagraph()
        : base(draws: false)
    {
    }

    /// <summary>The index of the boundary in its container.</summary>
    public int Number { get; set; }

    /// <summary>The height of the text a caret here stands for (without line spacing).</summary>
    internal double LineHeight { get; private set; }

    protected override void OnContextChanged() => _height = 0;

    protected override Size MeasureBlock(Size availableSize)
    {
        if (_height <= 0)
        {
            using var label = CreateLabel("x", Sheet.FontSize);
            LineHeight = label.LineMetrics[0].Height;
            _height = LineHeight * Sheet.LineSpacing;
        }

        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, _height);
    }
}
