using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The caret overlay (05): a light element above the content that takes no part in text layout or
/// hit testing. It is placed after layout from the projected selection (display offsets) and the
/// block layouts; moving it never relays out content. It blinks by itself and hides without focus,
/// for a selection (drawn by the blocks) and for a block caret (drawn by the block, Q59).
/// </summary>
internal sealed partial class CaretLayer : Canvas
{
    private const double CaretWidth = 1.5;
    private static readonly TimeSpan BlinkInterval = TimeSpan.FromMilliseconds(530);

    private readonly Rectangle _caret = new() { Width = CaretWidth, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private DispatcherTimer? _blink;
    private Rect? _bounds;

    public CaretLayer()
    {
        IsHitTestVisible = false;
        Children.Add(_caret);
        Unloaded += (_, _) => Release();
    }

    /// <summary>Where the caret is drawn, in page coordinates; null when it is hidden.</summary>
    public Rect? CaretBounds => _bounds;

    internal void Release()
    {
        if (_blink is { } timer)
        {
            timer.Stop();
            timer.Tick -= OnBlinkTick;
            _blink = null;
        }

        _bounds = null;
    }

    public void Update(BlockGeometry geometry, SelectionInfo? selection, MdStyleSheet sheet)
    {
        Rect? bounds = selection is { IsCollapsed: true, IsBlockCaret: false, HasFocus: true } ? Locate(geometry, selection.Head) : null;
        if (bounds == _bounds)
        {
            return;
        }

        _bounds = bounds;
        if (bounds is not { } rect)
        {
            _caret.Visibility = Visibility.Collapsed;
            _blink?.Stop();
            return;
        }

        // The layer sits inside the page padding; the rectangle is in page coordinates.
        var origin = TransformToVisual(geometry.Document.Page).TransformPoint(default);
        _caret.Fill = new SolidColorBrush(sheet.Foreground);
        _caret.Height = Math.Max(1, rect.Height);
        SetLeft(_caret, rect.X - origin.X - CaretWidth / 2);
        SetTop(_caret, rect.Y - origin.Y);
        _caret.Visibility = Visibility.Visible;

        // A moved caret shows at once and starts blinking again.
        _caret.Opacity = 1;
        if (_blink is null)
        {
            _blink = new DispatcherTimer { Interval = BlinkInterval };
            _blink.Tick += OnBlinkTick;
        }

        _blink.Stop();
        _blink.Start();
    }

    private void OnBlinkTick(object? sender, object e) => _caret.Opacity = _caret.Opacity > 0 ? 0 : 1;

    private static Rect? Locate(BlockGeometry geometry, ViewPosition head)
    {
        if (!head.IsBoundary)
        {
            return geometry.CaretRect(head.Block, head.DisplayOffset, head.Affinity == State.Affinity.Backward);
        }

        if (geometry.Gap(head.Block, head.DisplayOffset) is { } gap)
        {
            var bounds = geometry.Bounds(gap);
            double height = Math.Min(bounds.Height, gap.LineHeight);
            return new Rect(bounds.X, bounds.Y + (bounds.Height - height) / 2, 0, height);
        }

        return null;
    }
}
