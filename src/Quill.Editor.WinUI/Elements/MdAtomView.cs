namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A block shown as one object (05: <c>MdAtomView</c>): thematic breaks and the TOC here; images,
/// formulas and diagrams with their renderers later. Selected whole, it shows the selection overlay;
/// under a block caret (Q59, Q60) it draws its own frame.
/// </summary>
public abstract partial class MdAtomView : MdBlockView
{
    private bool _hasBlockCaret;

    protected MdAtomView()
        : base(draws: true)
    {
    }

    /// <summary>The caret stands on the block whole (Q59): drawn unlike a selection.</summary>
    public bool HasBlockCaret
    {
        get => _hasBlockCaret;
        set => SetAndRedraw(ref _hasBlockCaret, value);
    }

    /// <summary>The height of one line of body text: atoms without content of their own use it.</summary>
    protected double BodyLineHeight()
    {
        using var label = CreateLabel("x", Sheet.FontSize);
        return label.LineMetrics[0].Height * Sheet.LineSpacing;
    }
}
