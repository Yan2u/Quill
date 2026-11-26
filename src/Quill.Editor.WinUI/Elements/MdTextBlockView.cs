using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Text;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A block whose content is text (05: <c>MdTextBlockView</c>): paragraphs, headings, cells,
/// definition slots, and the raw-text blocks. Lays out its <see cref="TextSource"/> with
/// <see cref="BlockTextLayout"/> at its width and draws text, selection and composition itself.
/// Offsets of the selection and composition are display offsets (05, PositionMap).
/// </summary>
public abstract partial class MdTextBlockView : MdBlockView
{
    /// <summary>The selection shows on the text (on the children for containers), not over the whole block (Q84).</summary>
    protected override bool ShowsWholeSelection => false;

    private TextSource? _source;
    private ITextBlockLayout? _layout;

    // The layout before the last content change, kept when it can lend unchanged pieces to the next.
    private PiecewiseTextLayout? _reusable;
    private float _layoutWidth = -1;
    private double _naturalWidth = -1;
    private bool _isFocused;
    private int _selectionStart = -1;
    private int _selectionEnd = -1;
    private int _compositionStart = -1;
    private int _compositionEnd = -1;

    protected MdTextBlockView(bool draws = true)
        : base(draws)
    {
    }

    /// <summary>The number of find matches drawn last time, for tests.</summary>
    internal int DrawnMarks { get; private set; }

    /// <summary>The find matches changed: draw again, the layout stays (12-find-replace.md).</summary>
    internal void RedrawMarks() => Redraw();

    /// <summary>The caret's block (05: focused appearance).</summary>
    public bool IsFocused
    {
        get => _isFocused;
        set
        {
            if (_isFocused != value)
            {
                _isFocused = value;
                OnFocusChanged();
            }
        }
    }

    public int SelectionStart
    {
        get => _selectionStart;
        set => SetAndRedraw(ref _selectionStart, value);
    }

    public int SelectionEnd
    {
        get => _selectionEnd;
        set => SetAndRedraw(ref _selectionEnd, value);
    }

    public int CompositionStart
    {
        get => _compositionStart;
        set => SetAndRedraw(ref _compositionStart, value);
    }

    public int CompositionEnd
    {
        get => _compositionEnd;
        set => SetAndRedraw(ref _compositionEnd, value);
    }

    /// <summary>The current layout (null before the first measure), for the layout service (M5.2).</summary>
    internal ITextBlockLayout? TextLayout => _layout;

    /// <summary>Where the text starts inside the block.</summary>
    public Thickness TextPadding => Padding();

    public override double FirstBaseline => Padding().Top + (_layout?.FirstBaseline ?? 0);

    /// <summary>The width of the widest line when nothing wraps, plus padding: table columns size by it.</summary>
    public double MeasureNaturalWidth()
    {
        ResolveContext();
        if (_naturalWidth < 0)
        {
            var padding = Padding();
            using var probe = new BlockTextLayout(CanvasDevice.GetSharedDevice(), Source(), Format() with { Wrap = false, Alignment = Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment.Left }, Sheet, 100000);
            // Rounded up with a pixel to spare: laid out at exactly its own width, a line may still wrap.
            _naturalWidth = Math.Ceiling(probe.ContentWidth) + 1 + padding.Left + padding.Right;
        }

        return _naturalWidth;
    }

    private protected abstract TextSource BuildSource();

    private protected abstract BlockTextFormat Format();

    protected virtual Thickness Padding() => default;

    /// <summary>
    /// Lays out the text at a width. Raw text views lay out long text in pieces and reuse the unchanged
    /// pieces of <paramref name="previous"/>; this default disposes it and builds one layout.
    /// </summary>
    private protected virtual ITextBlockLayout CreateLayout(TextSource source, BlockTextFormat format, float width, PiecewiseTextLayout? previous)
    {
        previous?.Dispose();
        return new BlockTextLayout(CanvasDevice.GetSharedDevice(), source, format, Sheet, width);
    }

    /// <summary>Draws behind the text (code block backgrounds, placeholders).</summary>
    protected virtual void DrawBackground(CanvasDrawingSession session, Size size)
    {
    }

    protected virtual void OnFocusChanged() => Redraw();

    /// <summary>Something the content shows arrived (a decoded image): lay out and draw again.</summary>
    internal void RefreshContent() => InvalidateText();

    /// <summary>The content changed: rebuild the source and lay out again.</summary>
    protected void InvalidateText()
    {
        _source = null;
        _naturalWidth = -1;
        DropLayout();
        InvalidateMeasure();
        OnNaturalWidthInvalidated();
        Redraw();
    }

    /// <summary>Notifies containers that size by <see cref="MeasureNaturalWidth"/> that this block's natural width may have changed.</summary>
    protected virtual void OnNaturalWidthInvalidated()
    {
    }

    protected override void OnContextChanged() => InvalidateText();

    protected override Size MeasureBlock(Size availableSize)
    {
        var padding = Padding();
        double outer = double.IsInfinity(availableSize.Width) ? MeasureNaturalWidth() : availableSize.Width;
        float width = (float)Math.Max(1, outer - padding.Left - padding.Right);
        if (_layout is null || Math.Abs(width - _layoutWidth) > 0.01)
        {
            var previous = _reusable;
            _reusable = null;
            if (_layout is PiecewiseTextLayout current)
            {
                previous?.Dispose();
                previous = current;
            }
            else
            {
                _layout?.Dispose();
            }

            _layout = CreateLayout(Source(), Format(), width, previous);
            _layoutWidth = width;
            Redraw();
        }

        // Text that scrolls instead of wrapping: the scroll bar below it while a line overflows.
        // Blocks that scroll another part (a formula) set the overflow themselves after this measure: a
        // second call here would reset their offset on every measure (a scroll bar drag measures again).
        double bar = ManagesOverflow ? 0 : SetOverflow(ScrollsText ? _layout.ContentWidth : 0, width);
        return new Size(outer, _layout.Height + padding.Top + padding.Bottom + bar);
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        double width = EffectiveWidth;
        double height = EffectiveHeight;
        DrawBackground(session, new Size(width, height));
        if (_layout is null)
        {
            return;
        }

        var padding = Padding();
        (int, int)? selection = _selectionStart >= 0 && _selectionEnd > _selectionStart ? (_selectionStart, _selectionEnd) : null;
        (int, int)? composition = _compositionStart >= 0 && _compositionEnd > _compositionStart ? (_compositionStart, _compositionEnd) : null;
        var marks = Context.Matches?.MarksFor(BlockId)?.Select(static mark => new TextMark(mark.Start, mark.End, mark.Current)).ToList();
        DrawnMarks = marks?.Count ?? 0;
        if (TextScrollOffset == 0)
        {
            _layout.Draw(session, new Vector2((float)padding.Left, (float)padding.Top), selection, composition, region, marks);
            return;
        }

        // Scrolled text is clipped to the text column, as in a scroll viewer.
        var column = new Rect(padding.Left, 0, Math.Max(0, width - padding.Left - padding.Right), height);
        using (session.CreateLayer(1, column))
        {
            _layout.Draw(session, new Vector2((float)(padding.Left - TextScrollOffset), (float)padding.Top), selection, composition, region, marks);
        }
    }

    private TextSource Source() => _source ??= BuildSource();

    private void DropLayout()
    {
        if (_layout is PiecewiseTextLayout piecewise)
        {
            _reusable?.Dispose();
            _reusable = piecewise;
        }
        else
        {
            _layout?.Dispose();
        }

        _layout = null;
        _layoutWidth = -1;
    }

    protected override void OnBlockPooled()
    {
        base.OnBlockPooled();
        _layout?.Dispose();
        _layout = null;
        _reusable?.Dispose();
        _reusable = null;
        _source = null;
        _layoutWidth = -1;
        _naturalWidth = -1;
        ResetScrollState();
    }

    protected override void OnBlockUnmounted()
    {
        _layout?.Dispose();
        _layout = null;
        _reusable?.Dispose();
        _reusable = null;
        _source = null;
        _layoutWidth = -1;
        _naturalWidth = -1;
        DetachScrollBar();
    }
}
