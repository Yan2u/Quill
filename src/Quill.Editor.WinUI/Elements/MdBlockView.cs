using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;
using Windows.UI;

namespace Quill.Editor.WinUI.Elements;

/// <summary>The find matches of a block in display offsets (12-find-replace.md), drawn by the block; null for none.</summary>
public interface IMatchSource
{
    IReadOnlyList<(int Start, int End, bool Current)>? MarksFor(string blockKey);
}

/// <summary>
/// What a block inherits from where it sits: the style sheet, the color behind it, the text color, the image
/// loader, the highlighting, the find matches and the view options (null for the defaults).
/// </summary>
public readonly record struct BlockContext(MdStyleSheet Sheet, Color Surface, Color Foreground, Rendering.ImageLoader? Images = null, Rendering.SourceHighlighting? Highlighting = null, IMatchSource? Matches = null, MdViewOptions? Options = null)
{
    public static BlockContext Default { get; } = new(MdStyleSheet.Light, MdStyleSheet.Light.PageBackground, MdStyleSheet.Light.Foreground);
}

/// <summary>
/// The base of every block control (05-projection-and-view.md): BlockId, selection and block-caret
/// state, and the drawing surface. A block that draws owns one <see cref="CanvasVirtualControl"/>
/// (Q65: tiles only for the visible part), opaque and painted with the block's surface color so
/// text gets ClearType. Block controls hold no state of their own: everything comes from the VNode
/// and the style sheet, so they can be recycled at any time (Q64). They never handle input.
/// </summary>
public abstract partial class MdBlockView : Panel, Quill.VariableXaml.IVirtualLifecycle
{
    private readonly bool _draws;
    private CanvasVirtualControl? _canvas;
    private Size _canvasSurfaceSize;
    private Size _arrangedSize;
    private BlockContext _context = BlockContext.Default;
    private bool _hasContext;
    private MdDocument? _ownerDocument;
    private bool _drawnDirectDuringScroll;
    private string _blockId = string.Empty;
    private bool _isSelected;

    protected MdBlockView(bool draws)
    {
        _draws = draws;
    }

    /// <summary>The key of the block (<c>b42</c>); the reconciler and the layout service find blocks by it.</summary>
    public string BlockId
    {
        get => _blockId;
        set => _blockId = value ?? string.Empty;
    }

    /// <summary>The block is selected whole (05: selected appearance, independent of activation).</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetAndRedraw(ref _isSelected, value);
    }

    /// <summary>Draw calls so far, for the probes.</summary>
    public int DrawCount { get; private set; }

    /// <summary>The region and drawing session transform observed on the last draw pass, for diagnostics.</summary>
    internal Rect LastDrawnRegion { get; private set; }

    internal System.Numerics.Matrix3x2 LastSessionTransform { get; private set; }

    internal IReadOnlyList<Rect> LastInvalidatedRegions { get; private set; } = [];

    /// <summary>The last exception drawing this block threw, or null.</summary>
    internal Exception? LastDrawError { get; private set; }

    /// <summary>The time all blocks spent drawing on the UI thread, for performance measurements (09).</summary>
    public static TimeSpan TotalDrawTime { get; private set; }

    /// <summary>The inherited context, resolved at the last measure.</summary>
    public BlockContext Context => _context;

    /// <summary>The context of a block laid out outside the element tree (Q71), which has no document above it.</summary>
    internal BlockContext? DetachedContext { get; set; }

    /// <summary>Whether this block currently owns a Win2D <see cref="CanvasVirtualControl"/>.</summary>
    internal bool HasOwnCanvas => _canvas is not null;

    /// <summary>Whether this block type supports drawing onto a canvas (either its own or an ancestor's).</summary>
    internal bool CanHostCanvas => _draws;

    /// <summary>True for overlay controls whose XAML opacity/z-order requires a separate canvas instead of delegating to an ancestor.</summary>
    protected virtual bool RequiresOwnCanvas => false;

    /// <summary>When <see cref="RequiresOwnCanvas"/> is true, controls when the private canvas should be materialized.</summary>
    protected virtual bool ShouldCreateOwnCanvas => true;

    /// <summary>The size assigned on the most recent arrange pass.</summary>
    internal Size ArrangedSize => _arrangedSize;

    internal bool NeedsCrispRedrawAfterFastScroll => _canvas is not null && _drawnDirectDuringScroll;

    internal void RedrawCrispAfterFastScroll()
    {
        _drawnDirectDuringScroll = false;
        Redraw();
    }

    protected internal double EffectiveWidth => ActualWidth > 0 ? ActualWidth : _arrangedSize.Width;

    protected internal double EffectiveHeight => ActualHeight > 0 ? ActualHeight : _arrangedSize.Height;

    protected MdStyleSheet Sheet => _context.Sheet;

    protected MdViewOptions ViewOptions => _context.Options ?? MdViewOptions.Default;

    /// <summary>The number of children that are not blocks (the drawing canvas).</summary>
    protected int InternalChildCount => _canvas is null ? 0 : 1;

    /// <summary>The baseline of the first line, from the top; list markers align with it.</summary>
    public virtual double FirstBaseline => DesiredSize.Height * 0.7;

    /// <summary>The context this block hands to its children: containers change surface or foreground.</summary>
    protected internal virtual BlockContext ChildContext => _context;

    /// <summary>Forgets the resolved context and lays the block out again: the style sheet changed.</summary>
    public void InvalidateStyle()
    {
        _hasContext = false;
        InvalidateMeasure();
        Redraw();
    }

    protected sealed override Size MeasureOverride(Size availableSize)
    {
        ResolveContext();
        EnsureCanvas();
        var size = MeasureBlock(availableSize);
        if (_canvas is not null)
        {
            if (!double.IsNaN(_canvas.Width) || !double.IsNaN(_canvas.Height))
            {
                _canvas.Width = double.NaN;
                _canvas.Height = double.NaN;
            }

            _canvas.Measure(size);
        }

        return size;
    }

    protected sealed override Size ArrangeOverride(Size finalSize)
    {
        _arrangedSize = finalSize;
        ArrangeBlock(finalSize);
        _canvas?.Arrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    protected abstract Size MeasureBlock(Size availableSize);

    protected virtual void ArrangeBlock(Size finalSize)
    {
    }

    /// <summary>Draws the block over its surface color; the selection overlay is drawn afterwards.</summary>
    protected virtual void DrawBlock(CanvasDrawingSession session, Rect region)
    {
    }

    /// <summary>Draws delegated child blocks that do not own a separate canvas.</summary>
    protected virtual void DrawDelegatedChildren(CanvasDrawingSession session, Rect region)
    {
    }

    /// <summary>Called when the inherited context changed: drop cached layouts.</summary>
    protected virtual void OnContextChanged()
    {
    }

    protected virtual void OnBlockPooled()
    {
    }

    protected virtual void OnBlockUnmounted()
    {
    }

    void Quill.VariableXaml.IVirtualLifecycle.OnPooled()
    {
        _canvasSurfaceSize = default;
        _arrangedSize = default;
        _ownerDocument = null;
        _drawnDirectDuringScroll = false;
        if (_canvas is not null)
        {
            // Shrink pooled canvas dimensions to 0 so Win2D releases virtual tile backing store while
            // keeping the control attached in Children (zero new GDI handles on reuse).
            _canvas.Width = 0;
            _canvas.Height = 0;
        }

        OnBlockPooled();
        InvalidateMeasure();
        InvalidateArrange();
    }

    void Quill.VariableXaml.IVirtualLifecycle.OnUnmounted() => UnmountBlock();

    protected void Redraw()
    {
        if (_canvas is not null)
        {
            _canvas.Invalidate();
        }
        else
        {
            FindDrawingHost()?.InvalidateDelegatedChild(this);
        }
    }

    protected void InvalidateRegion(Rect region)
    {
        if (_canvas is not { } canvas)
        {
            if (!_draws || !HasOwnCanvas)
            {
                FindDrawingHost()?.InvalidateDelegatedChild(this, region);
            }

            return;
        }

        if (!canvas.ReadyToDraw
            || !double.IsFinite(region.X)
            || !double.IsFinite(region.Y)
            || !double.IsFinite(region.Width)
            || !double.IsFinite(region.Height))
        {
            canvas.Invalidate();
            return;
        }

        // Clamp against the intersection of canvas.Size and the last size observed in RegionsInvalidated,
        // because Win2D resizes its backing VirtualSurfaceImageSource asynchronously and throws
        // ArgumentException (E_INVALIDARG) when a partial rect exceeds the backing surface bounds.
        Size size = canvas.Size;
        double maxWidth = _canvasSurfaceSize.Width > 0 ? Math.Min(size.Width, _canvasSurfaceSize.Width) : size.Width;
        double maxHeight = _canvasSurfaceSize.Height > 0 ? Math.Min(size.Height, _canvasSurfaceSize.Height) : size.Height;
        if (maxWidth <= 0 || maxHeight <= 0 || !double.IsFinite(maxWidth) || !double.IsFinite(maxHeight))
        {
            canvas.Invalidate();
            return;
        }

        if (region.Right > maxWidth || region.Bottom > maxHeight)
        {
            canvas.Invalidate();
            return;
        }

        double left = Math.Clamp(region.Left, 0, maxWidth);
        double top = Math.Clamp(region.Top, 0, maxHeight);
        double right = Math.Clamp(region.Right, 0, maxWidth);
        double bottom = Math.Clamp(region.Bottom, 0, maxHeight);
        if (right <= left || bottom <= top)
        {
            canvas.Invalidate();
            return;
        }

        try
        {
            canvas.Invalidate(new Rect(left, top, right - left, bottom - top));
        }
        catch (ArgumentException)
        {
            // Defense in depth for fractional-DPI boundary rounding or an in-flight backing-surface resize.
            canvas.Invalidate();
        }
    }

    internal void InvalidateDelegatedChild(MdBlockView child) => InvalidateDelegatedChild(child, null);

    internal virtual void InvalidateDelegatedChild(MdBlockView child, Rect? childRegion)
    {
        if (_canvas is not null)
        {
            _canvas.Invalidate();
        }
        else
        {
            FindDrawingHost()?.InvalidateDelegatedChild(this, childRegion);
        }
    }

    internal void DrawDelegated(CanvasDrawingSession session, Rect localRegion)
    {
        DrawCount++;
        LastDrawnRegion = localRegion;
        try
        {
            DrawBlock(session, localRegion);
            DrawDelegatedChildren(session, localRegion);
            if (_isSelected && ShowsWholeSelection)
            {
                session.FillRectangle(new Rect(0, 0, EffectiveWidth, EffectiveHeight), Sheet.BlockSelection);
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            LastDrawError = error;
            System.Diagnostics.Debug.WriteLine($"{GetType().Name} {BlockId}: {error}");
        }
    }

    /// <summary>
    /// Breaks the Win2D native-to-managed reference cycle (<c>m_lastSeenParent</c> and event delegate)
    /// and releases block context/unmanaged state when permanently unmounted from the reconciler.
    /// </summary>
    internal void UnmountBlock()
    {
        OnBlockUnmounted();
        if (_canvas is { } canvas)
        {
            canvas.RegionsInvalidated -= OnRegionsInvalidated;
            canvas.RemoveFromVisualTree();
            Children.Remove(canvas);
            _canvas = null;
        }

        _canvasSurfaceSize = default;
        _arrangedSize = default;
        _ownerDocument = null;
        _drawnDirectDuringScroll = false;
        _context = BlockContext.Default;
        _hasContext = false;
        DetachedContext = null;
    }

    private void EnsureCanvas()
    {
        if (!_draws || _canvas is not null || IsInDetachedTree())
        {
            return;
        }

        if (RequiresOwnCanvas)
        {
            if (!ShouldCreateOwnCanvas)
            {
                return;
            }
        }
        else if (HasDrawingHostAncestor())
        {
            return;
        }

        _canvas = new CanvasVirtualControl { ClearColor = _context.Surface, IsHitTestVisible = false };
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        Children.Insert(0, _canvas);
    }

    private bool IsInDetachedTree()
    {
        static DependencyObject? ParentOf(DependencyObject element) =>
            VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

        DependencyObject? current = this;
        DependencyObject? last = null;
        while (current is not null)
        {
            if (current is MdBlockView { DetachedContext: not null })
            {
                return true;
            }

            last = current;
            current = ParentOf(current);
        }

        return VisualTreeHelper.GetParent(this) is null && last is FrameworkElement { IsLoaded: false };
    }

    private bool HasDrawingHostAncestor()
    {
        static DependencyObject? ParentOf(DependencyObject element) =>
            VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

        for (DependencyObject? parent = ParentOf(this); parent is not null; parent = ParentOf(parent))
        {
            if (parent is MdBlockView { CanHostCanvas: true })
            {
                return true;
            }
        }

        return false;
    }

    private MdBlockView? FindDrawingHost()
    {
        static DependencyObject? ParentOf(DependencyObject element) =>
            VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

        for (DependencyObject? parent = ParentOf(this); parent is not null; parent = ParentOf(parent))
        {
            if (parent is MdBlockView { _canvas: not null } host)
            {
                return host;
            }
        }

        return null;
    }

    protected void SetAndRedraw<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Redraw();
        }
    }

    protected void SetAndRemeasure<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            InvalidateMeasure();
            Redraw();
        }
    }

    /// <summary>A one-line text layout in the block's font, for decorations (markers, brackets, labels).</summary>
    protected CanvasTextLayout CreateLabel(string text, float fontSize, bool monospace = false)
    {
        using var format = new CanvasTextFormat
        {
            FontFamily = monospace ? Sheet.MonospaceFamily : Sheet.FontFamily,
            FontSize = fontSize,
            WordWrapping = CanvasWordWrapping.NoWrap,
            Options = CanvasDrawTextOptions.EnableColorFont,
        };
        return new CanvasTextLayout(CanvasDevice.GetSharedDevice(), text, format, 10000, 10000);
    }

    /// <summary>Finds the context from the parent chain; containers query children before measuring them.</summary>
    protected void ResolveContext()
    {
        var context = FindContext();
        if (_hasContext && context.Surface == _context.Surface && context.Foreground == _context.Foreground && ReferenceEquals(context.Sheet, _context.Sheet)
            && ReferenceEquals(context.Images, _context.Images) && ReferenceEquals(context.Options, _context.Options))
        {
            return;
        }

        bool changed = !_hasContext || context != _context;
        _context = context;
        _hasContext = true;
        if (_canvas is not null)
        {
            _canvas.ClearColor = context.Surface;
        }

        if (changed)
        {
            OnContextChanged();
            Redraw();
        }
    }

    private BlockContext FindContext()
    {
        // Detached trees (Q71) have no visual parents: their logical parents are the panels.
        static DependencyObject? ParentOf(DependencyObject element) =>
            VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

        for (var parent = ParentOf(this); parent is not null; parent = ParentOf(parent))
        {
            switch (parent)
            {
                case MdBlockView block:
                    _ownerDocument = block._ownerDocument;
                    return block.ChildContext;
                case MdDocument document:
                    _ownerDocument = document;
                    var sheet = document.EffectiveStyleSheet;
                    return new BlockContext(sheet, sheet.PageBackground, sheet.Foreground, document.Images, document.EffectiveHighlighting, document.Matches, document.ViewOptions);
            }
        }

        _ownerDocument = null;
        return DetachedContext ?? BlockContext.Default;
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        Draw(sender, args);
        TotalDrawTime += System.Diagnostics.Stopwatch.GetElapsedTime(start);
    }

    /// <summary>
    /// Whether the block shows "selected whole" over its full area (05, Q84): only blocks without text
    /// (rules, TOC). Text blocks show the selection on their text, containers through their children,
    /// as in Typora.
    /// </summary>
    protected virtual bool ShowsWholeSelection => true;

    /// <summary>Whether the block draws the whole-block selection now, for tests.</summary>
    internal bool DrawsWholeSelection => _isSelected && ShowsWholeSelection;

    private void Draw(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        _canvasSurfaceSize = sender.Size;
        var regions = args.InvalidatedRegions;
        LastInvalidatedRegions = regions;
        float dpi = sender.Dpi;
        float scale = dpi / 96f;
        int maxPixels = sender.Device.MaximumBitmapSizeInPixels;
        bool fastScroll = _ownerDocument is { IsFastScrolling: true };
        foreach (var region in regions)
        {
            if (region.Width <= 0 || region.Height <= 0)
            {
                continue;
            }

            DrawCount++;
            using var session = sender.CreateDrawingSession(region);
            LastDrawnRegion = region;
            LastSessionTransform = session.Transform;

            // Render into a stable (0, 0)-origin bitmap so DirectWrite's pixel snapping never sees
            // VirtualSurfaceImageSource's shifting tile-atlas offset between redraws. During active
            // intermediate scrolling, draw directly to avoid per-tile render-target churn.
            int pxLeft = (int)Math.Floor(region.X * scale);
            int pxTop = (int)Math.Floor(region.Y * scale);
            int pxRight = (int)Math.Ceiling((region.X + region.Width) * scale);
            int pxBottom = (int)Math.Ceiling((region.Y + region.Height) * scale);
            int pxWidth = Math.Max(1, pxRight - pxLeft);
            int pxHeight = Math.Max(1, pxBottom - pxTop);

            if (pxWidth <= maxPixels && pxHeight <= maxPixels && !fastScroll)
            {
                _drawnDirectDuringScroll = false;
                float dipLeft = pxLeft / scale;
                float dipTop = pxTop / scale;
                float dipWidth = pxWidth / scale;
                float dipHeight = pxHeight / scale;
                using var target = new CanvasRenderTarget(sender, dipWidth, dipHeight, dpi);
                using (var ds = target.CreateDrawingSession())
                {
                    if (pxLeft != 0 || pxTop != 0)
                    {
                        ds.Transform = System.Numerics.Matrix3x2.CreateTranslation(-dipLeft, -dipTop);
                    }

                    DrawContent(ds, region);
                }

                var destination = new Rect(dipLeft, dipTop, target.Size.Width, target.Size.Height);
                session.Antialiasing = CanvasAntialiasing.Aliased;
                session.Blend = CanvasBlend.Copy;
                session.DrawImage(target, destination, target.Bounds, 1f, CanvasImageInterpolation.NearestNeighbor);
            }
            else
            {
                DrawContent(session, region);
                _drawnDirectDuringScroll = fastScroll;
            }
        }
    }

    private void DrawContent(CanvasDrawingSession session, Rect region)
    {
        session.Clear(_context.Surface);
        session.TextAntialiasing = CanvasTextAntialiasing.ClearType;
        try
        {
            DrawBlock(session, region);
            DrawDelegatedChildren(session, region);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // One block failing to draw must not take the others down; kept for tests and diagnostics.
            LastDrawError = error;
            System.Diagnostics.Debug.WriteLine($"{GetType().Name} {BlockId}: {error}");
        }

        if (_isSelected && ShowsWholeSelection)
        {
            session.FillRectangle(new Rect(0, 0, EffectiveWidth, EffectiveHeight), Sheet.BlockSelection);
        }
    }
}
