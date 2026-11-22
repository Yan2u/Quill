using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A code block (Q91): its code (<see cref="MdCodeBlock"/>) and its language (<see cref="MdCodeLanguage"/>),
/// both document text. The code takes the full width; the block is as tall as the code. While the block is
/// active (the caret in either), the language box shows outside the code at its bottom right, as in
/// Typora, over whatever follows: nothing moves (Q91). The block and its ancestors come to the front so
/// the opaque blocks after them do not cover it. Otherwise the box is hidden and lies over the bottom right
/// of the code, still laid out, so that Up from below finds it.
/// </summary>
public sealed partial class MdCodeFence : MdContainerView
{
    private const double LanguageGap = 4;
    private const double LanguageMinWidth = 120;

    public MdCodeFence()
        : base(draws: false)
    {
    }

    private MdCodeBlock? Code => Blocks.Count > 0 ? Blocks[0] as MdCodeBlock : null;

    private MdCodeLanguage? LanguageView => Blocks.Count > 1 ? Blocks[1] as MdCodeLanguage : null;

    public override double FirstBaseline => Code?.FirstBaseline ?? base.FirstBaseline;

    protected override void OnActiveChanged()
    {
        if (Code is { } code)
        {
            code.IsFenceActive = IsActive;
        }

        if (LanguageView is { } language)
        {
            language.IsShown = IsActive;
        }

        BringToFront(IsActive);
        InvalidateArrange();
    }

    /// <summary>
    /// Raises (or lowers again) this block and each enclosing block among its siblings. Only block
    /// controls: raising the document body would put it over the caret and adorner layers.
    /// </summary>
    private void BringToFront(bool front)
    {
        DependencyObject? element = this;
        while (element is MdBlockView block)
        {
            Microsoft.UI.Xaml.Controls.Canvas.SetZIndex(block, front ? 1 : 0);
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(block);
        }
    }

    protected override Size MeasureBlock(Size availableSize)
    {
        if (Code is not { } code || LanguageView is not { } language)
        {
            return base.MeasureBlock(availableSize);
        }

        code.IsFenceActive = IsActive;
        language.IsShown = IsActive;
        code.Measure(new Size(availableSize.Width, double.PositiveInfinity));
        double width = double.IsInfinity(availableSize.Width) ? code.DesiredSize.Width : availableSize.Width;
        double languageWidth = Math.Min(width, Math.Max(LanguageMinWidth, language.MeasureNaturalWidth()));
        language.Measure(new Size(languageWidth, double.PositiveInfinity));
        return new Size(width, code.DesiredSize.Height);
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        if (Code is not { } code || LanguageView is not { } language)
        {
            base.ArrangeBlock(finalSize);
            return;
        }

        double codeHeight = code.DesiredSize.Height;
        code.Arrange(new Rect(0, 0, finalSize.Width, codeHeight));
        var size = language.DesiredSize;
        double top = IsActive ? codeHeight + LanguageGap : Math.Max(0, codeHeight - size.Height);
        language.Arrange(new Rect(Math.Max(0, finalSize.Width - size.Width), top, size.Width, size.Height));
    }
}
