using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// Horizontal scrolling of content wider than the block (M6.3, as Typora): a display formula wider than
/// the column, or code when code blocks do not wrap. A native WinUI <see cref="ScrollBar"/> shows at
/// the bottom of the block only while the content overflows. The offset is view state: a recycled
/// block starts at zero. Blocks whose text scrolls (code) shift their text by the offset, and the
/// layout service sees it through <see cref="TextScrollOffset"/>.
/// </summary>
public abstract partial class MdTextBlockView
{
    private ScrollBar? _scrollBar;
    private double _scrollOffset;
    private double _scrollContent;
    private double _scrollViewport;

    /// <summary>The horizontal offset of the scrolled part.</summary>
    internal double HorizontalOffset => _scrollOffset;

    /// <summary>How far the text itself is scrolled to the left (0 unless the text is the scrolled part).</summary>
    internal double TextScrollOffset => ScrollsText ? _scrollOffset : 0;

    /// <summary>Whether the scrolled part is wider than the block.</summary>
    internal bool IsOverflowing => _scrollContent > _scrollViewport + 0.5;

    /// <summary>The scroll bar, shown while the content overflows; null before the first overflow.</summary>
    internal ScrollBar? HorizontalScrollBar => _scrollBar;

    /// <summary>The height of the scroll bar while it shows, else 0.</summary>
    internal double ScrollBarHeight => _scrollBar is { Visibility: Visibility.Visible } bar ? bar.DesiredSize.Height : 0;

    /// <summary>Where the bottom of the scroll bar goes: the bottom of the block, unless more follows the scrolled part.</summary>
    private protected virtual double ScrollBarBottom(Size finalSize) => finalSize.Height;

    /// <summary>Whether the block sets the overflow of a part other than its text itself (in its own measure).</summary>
    private protected virtual bool ManagesOverflow => false;

    /// <summary>Whether the text scrolls when wider than the block (code without wrapping); otherwise another part does.</summary>
    private protected virtual bool ScrollsText => false;

    /// <summary>Scrolls the overflowing part to an offset, clamped to the content.</summary>
    internal void ScrollHorizontallyTo(double offset)
    {
        double clamped = Math.Clamp(offset, 0, Math.Max(0, _scrollContent - _scrollViewport));
        if (Math.Abs(clamped - _scrollOffset) < 0.01)
        {
            return;
        }

        _scrollOffset = clamped;
        if (_scrollBar is not null && Math.Abs(_scrollBar.Value - clamped) > 0.01)
        {
            _scrollBar.Value = clamped;
        }

        Redraw();

        // A layout pass moves the caret and selection overlays with the text.
        InvalidateArrange();
    }

    /// <summary>Scrolls scrolling text the least needed to show the text x (unscrolled), with a margin.</summary>
    internal void ScrollTextIntoView(double x, double margin = 16)
    {
        if (!ScrollsText || !IsOverflowing)
        {
            return;
        }

        if (x - _scrollOffset < margin)
        {
            ScrollHorizontallyTo(x - margin);
        }
        else if (x - _scrollOffset > _scrollViewport - margin)
        {
            ScrollHorizontallyTo(x - _scrollViewport + margin);
        }
    }

    /// <summary>
    /// Sets the width of the scrolled part and the width it shows in; returns the height the scroll bar
    /// needs below the content (0 when nothing overflows).
    /// </summary>
    private protected double SetOverflow(double content, double viewport)
    {
        _scrollContent = content;
        _scrollViewport = viewport;
        if (!IsOverflowing)
        {
            _scrollOffset = 0;
            if (_scrollBar is not null)
            {
                _scrollBar.Visibility = Visibility.Collapsed;
            }

            return 0;
        }

        if (_scrollBar is null)
        {
            _scrollBar = new ScrollBar
            {
                Orientation = Orientation.Horizontal,
                IndicatorMode = ScrollingIndicatorMode.MouseIndicator,
                SmallChange = 40,
                Minimum = 0,
            };
            _scrollBar.ValueChanged += OnScrollBarValueChanged;
            Children.Add(_scrollBar);
        }

        double maximum = content - viewport;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, maximum);
        _scrollBar.Maximum = maximum;
        _scrollBar.ViewportSize = viewport;
        _scrollBar.LargeChange = viewport;
        _scrollBar.Value = _scrollOffset;
        _scrollBar.Visibility = Visibility.Visible;
        _scrollBar.Measure(new Size(viewport, double.PositiveInfinity));
        return _scrollBar.DesiredSize.Height;
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        if (_scrollBar is { Visibility: Visibility.Visible } bar)
        {
            var padding = Padding();
            double height = bar.DesiredSize.Height;
            bar.Arrange(new Rect(padding.Left, Math.Max(0, ScrollBarBottom(finalSize) - height), Math.Max(0, _scrollViewport), height));
        }
    }

    private void OnScrollBarValueChanged(object sender, RangeBaseValueChangedEventArgs args) => ScrollHorizontallyTo(args.NewValue);

    private void ResetScrollState()
    {
        _scrollOffset = 0;
        _scrollContent = 0;
        _scrollViewport = 0;
        if (_scrollBar is not null)
        {
            _scrollBar.Visibility = Visibility.Collapsed;
        }
    }

    private void DetachScrollBar()
    {
        if (_scrollBar is { } bar)
        {
            bar.ValueChanged -= OnScrollBarValueChanged;
            Children.Remove(bar);
            _scrollBar = null;
        }

        _scrollOffset = 0;
        _scrollContent = 0;
        _scrollViewport = 0;
    }
}
