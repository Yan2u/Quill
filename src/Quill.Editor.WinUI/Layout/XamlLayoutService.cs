using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Editor.Services;
using Quill.Editor.State;
using Quill.Editor.WinUI.Elements;
using Quill.VariableXaml.Model;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The layout service of the real view (08): caret rectangles, hit testing, visual lines and the
/// viewport, in page coordinates of the <see cref="MdDocument"/> (DIPs, independent of scrolling).
/// Positions are source positions; the latest projection's PositionMap converts them to the display
/// offsets the block layouts use. Block positions come from XAML layout, block-internal geometry
/// from the self-drawn text (Q62). Blocks are never pinned (Q64): a block the virtual stack has not
/// realized is laid out outside the element tree at the position its <see cref="BlockStackLayout"/>
/// keeps for it (Q71), so queries never scroll; only <see cref="ScrollIntoView"/> does.
/// </summary>
public sealed class XamlLayoutService : ILayoutService
{
    private const double Tolerance = 0.5;
    private const int SearchLimit = 64;

    private readonly Func<MdDocument?> _document;
    private readonly Func<ProjectionResult?> _projection;
    private readonly Func<Document> _model;
    private readonly Action? _ensureCurrent;
    private readonly DetachedBlocks _detached = new();

    /// <param name="document">The rendered document root.</param>
    /// <param name="projection">The projection the view shows.</param>
    /// <param name="model">The document the projection was made from.</param>
    /// <param name="ensureCurrent">Brings the view up to date (render and layout) before a query (08).</param>
    public XamlLayoutService(Func<MdDocument?> document, Func<ProjectionResult?> projection, Func<Document> model, Action? ensureCurrent = null)
    {
        _document = document;
        _projection = projection;
        _model = model;
        _ensureCurrent = ensureCurrent;
    }

    /// <summary>Top-level blocks laid out outside the tree by the last query, for the probes.</summary>
    public int DetachedLayouts { get; private set; }

    internal void ClearDetached() => _detached.Clear();

    public LayoutRect GetCaretRect(Position position)
    {
        var geometry = Prepare();
        return ToLayout(CaretRect(geometry, position) ?? FallbackRect(geometry));
    }

    public Position HitTest(LayoutPoint point)
    {
        var geometry = Prepare();
        if (Stack(geometry) is { } stack && stack.Count > 0)
        {
            // The block under the point and its neighbours, realized or laid out outside the tree.
            int index = stack.IndexAt(point.Y - StackOrigin(geometry).Y);
            for (int i = Math.Max(0, index - 1); i <= Math.Min(stack.Count - 1, index + 1); i++)
            {
                IncludeItem(geometry, i);
            }
        }

        return HitTest(geometry, new Point(point.X, point.Y)) ?? new BoundaryPosition(_model().Id, 0);
    }

    public VisualLine GetVisualLine(Position position)
    {
        var geometry = Prepare();
        if (position is TextPosition text && Locate(geometry, text) is { } located)
        {
            var (view, layout, map, display, backward) = located;
            int index = layout.LineIndexOfCaret(display, backward);
            var line = layout.Line(index);
            var origin = geometry.TextOrigin(view);
            bool wrapped = index < layout.LineCount - 1 && layout.Line(index + 1).Start == line.End;
            var start = new TextPosition(text.Block, map.ToSource(line.Start, Affinity.Forward));
            var end = new TextPosition(text.Block, map.ToSource(line.End, Affinity.Backward), wrapped ? Affinity.Backward : Affinity.Forward);
            return new VisualLine(start, end, new LayoutRect(origin.X, origin.Y + line.Top, layout.Width, line.Height));
        }

        var rect = GetCaretRect(position);
        return new VisualLine(position, position, rect);
    }

    public Position? GetAdjacentLine(Position position, double x, bool down)
    {
        var geometry = Prepare();
        var current = LineBounds(geometry, position);
        int? item = TopLevelIndexOf(position);
        for (int reach = 1; reach <= SearchLimit; reach *= 2)
        {
            var candidates = Candidates(geometry)
                .Where(box => down ? box.Bounds.Y >= current.Bottom - Tolerance : box.Bounds.Bottom <= current.Y + Tolerance)
                .ToList();
            if (candidates.Count > 0)
            {
                // The nearest row; side by side boxes (table cells, definition slots) by x.
                double edge = down ? candidates.Min(box => box.Bounds.Y) : candidates.Max(box => box.Bounds.Bottom);
                var row = candidates.Where(box => down ? box.Bounds.Y <= edge + Tolerance : box.Bounds.Bottom >= edge - Tolerance);
                var target = Nearest(row, x);
                return Resolve(geometry, target, new Point(x, target.Bounds.Y + target.Bounds.Height / 2));
            }

            // Nothing that way among the blocks at hand: lay out more blocks beyond them.
            if (item is not { } at || Stack(geometry) is not { } stack || !IncludeRange(geometry, stack, down ? at + 1 : at - reach, down ? at + reach : at - 1))
            {
                break;
            }
        }

        return null;
    }

    public LayoutRect GetViewport() => ToLayout(Viewport(Prepare()));

    /// <summary>
    /// Scrolls a block to the top of the viewport, with a margin (the outline, M6.5). Repeated while
    /// realizing blocks moves it (Q69).
    /// </summary>
    public void ScrollToTop(BlockId block, double margin = 16)
    {
        var geometry = Prepare();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            if (BoundsOf(geometry, block) is not { } bounds || Math.Abs(bounds.Y - margin - Viewport(geometry).Y) < 1
                || !ScrollTo(geometry, bounds.Y - margin))
            {
                return;
            }

            geometry.Invalidate();
        }
    }

    /// <summary>A block's bounds: measured when realized, else its place in the stack (estimated before it was measured).</summary>
    private Rect? BoundsOf(BlockGeometry geometry, BlockId block)
    {
        if (geometry.Find(block) is { } view && !geometry.IsDetached(view))
        {
            return geometry.Bounds(view);
        }

        return DocumentLookup.TopLevelIndex(_model(), block) is { } index && Stack(geometry) is { } stack && index < stack.Count
            ? new Rect(0, StackOrigin(geometry).Y + stack.TopOf(index), 1, stack.HeightOf(index))
            : null;
    }

    public void ScrollIntoView(BlockId block)
    {
        var geometry = Prepare();
        for (int attempt = 0; attempt < 4; attempt++)
        {
            var viewport = Viewport(geometry);
            if (BoundsOf(geometry, block) is not { } bounds)
            {
                return;
            }

            if (bounds.Bottom > viewport.Y && bounds.Y < viewport.Bottom && geometry.Find(block) is { } shown && !geometry.IsDetached(shown))
            {
                return;
            }

            if (!ScrollTo(geometry, bounds.Y - viewport.Height / 3))
            {
                return;
            }

            geometry.Invalidate();
        }
    }

    /// <summary>
    /// Scrolls a position into view (07 rule 9). Scrolling realizes blocks whose measured height
    /// replaces their estimate, which can move the position again: repeated until it stays (Q69).
    /// </summary>
    public void ScrollPositionIntoView(Position position, double margin = 8)
    {
        // Text that scrolls horizontally inside its block (code without wrapping) first brings the caret's column in.
        if (position is TextPosition text && Prepare() is var geometry && geometry.Find(text.Block) is MdTextBlockView { IsOverflowing: true } view)
        {
            var caret = GetCaretRect(position);
            view.ScrollTextIntoView(caret.X - geometry.TextOrigin(view).X);
        }

        for (int round = 0; round < 3; round++)
        {
            var rect = GetCaretRect(position);
            var viewport = GetViewport();
            if (rect.Y >= viewport.Y + margin && rect.Bottom <= viewport.Bottom - margin)
            {
                return;
            }

            ScrollRectIntoView(rect, margin);
        }
    }

    /// <summary>Scrolls the least needed to show a rectangle (the caret after a command, 07 rule 9), with a margin.</summary>
    public void ScrollRectIntoView(LayoutRect rect, double margin = 8)
    {
        var geometry = Prepare();
        var viewport = Viewport(geometry);
        if (rect.Y < viewport.Y + margin)
        {
            ScrollTo(geometry, rect.Y - margin);
        }
        else if (rect.Bottom > viewport.Bottom - margin)
        {
            ScrollTo(geometry, rect.Bottom + margin - viewport.Height);
        }

        geometry.Invalidate();
    }

    /// <summary>The bounds of the block if the virtual stack realized it, in layout units; null otherwise.</summary>
    public LayoutRect? GetBlockBounds(BlockId block)
    {
        var geometry = Prepare();
        return geometry.Find(block) is { } view && !geometry.IsDetached(view) ? ToLayout(geometry.Bounds(view)) : null;
    }

    private static LayoutRect ToLayout(Rect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private static Rect Viewport(BlockGeometry geometry)
    {
        var scroller = geometry.Document.ScrollViewer;
        return new Rect(scroller.HorizontalOffset, scroller.VerticalOffset, scroller.ViewportWidth, scroller.ViewportHeight);
    }

    private static Candidate Nearest(IEnumerable<Candidate> boxes, double x) =>
        boxes.OrderBy(box => x < box.Bounds.X ? box.Bounds.X - x : x > box.Bounds.Right ? x - box.Bounds.Right : 0).First();

    private static ItemsRepeater? Repeater(BlockGeometry geometry) => geometry.Document.Body as ItemsRepeater;

    private static BlockStackLayout? Stack(BlockGeometry geometry) => Repeater(geometry)?.Layout as BlockStackLayout;

    private static Point StackOrigin(BlockGeometry geometry) =>
        Repeater(geometry) is { } repeater ? geometry.Bounds(repeater) is var bounds ? new Point(bounds.X, bounds.Y) : default : default;

    private BlockGeometry Prepare()
    {
        _ensureCurrent?.Invoke();
        var document = _document() ?? throw new InvalidOperationException("The document is not rendered.");

        // Blocks laid out outside the tree belong to one query; the realized index stays until the tree changes.
        document.Geometry.ClearDetached();
        DetachedLayouts = 0;
        return document.Geometry;
    }

    /// <summary>Scrolls so that the page coordinate is at the top of the viewport; false when nothing moved.</summary>
    private static bool ScrollTo(BlockGeometry geometry, double y)
    {
        var scroller = geometry.Document.ScrollViewer;
        double target = Math.Clamp(y, 0, scroller.ScrollableHeight);
        if (Math.Abs(target - scroller.VerticalOffset) < 1)
        {
            return false;
        }

        scroller.ChangeView(null, target, null, disableAnimation: true);

        // Applied in a layout pass: make sure there is one now, so the query that follows sees it.
        scroller.InvalidateArrange();
        geometry.Document.UpdateLayout();
        return true;
    }

    /// <summary>Makes the top-level block at an index available to queries: realized, or laid out outside the tree.</summary>
    private bool IncludeItem(BlockGeometry geometry, int index)
    {
        if (Repeater(geometry) is not { ItemsSourceView: { } items } repeater || Stack(geometry) is not { } stack || index < 0 || index >= items.Count || index >= stack.Count)
        {
            return false;
        }

        if (items.GetAt(index) is not VNode { Key: { } key } node || geometry.Find(key) is not null)
        {
            return true;
        }

        if (_detached.Get(node, stack.Width, geometry.Document.EffectiveStyleSheet, geometry.Document.ViewOptions) is { } root)
        {
            var origin = StackOrigin(geometry);
            geometry.Include(root, new Point(origin.X, origin.Y + stack.TopOf(index)));
            DetachedLayouts++;
        }

        return true;
    }

    private bool IncludeRange(BlockGeometry geometry, BlockStackLayout stack, int first, int last)
    {
        bool any = false;
        for (int i = Math.Max(0, first); i <= Math.Min(stack.Count - 1, last); i++)
        {
            any |= IncludeItem(geometry, i);
        }

        return any;
    }

    private int? TopLevelIndexOf(Position position) => position switch
    {
        TextPosition text => DocumentLookup.TopLevelIndex(_model(), text.Block),
        BoundaryPosition boundary when boundary.Parent == _model().Id => Math.Max(0, boundary.Index - 1),
        BoundaryPosition boundary => DocumentLookup.TopLevelIndex(_model(), boundary.Parent),
        _ => null,
    };

    private Rect? CaretRect(BlockGeometry geometry, Position position) => position switch
    {
        TextPosition text => Locate(geometry, text) is { } located ? geometry.CaretRect(text.Block, located.Display, located.Backward) : null,
        BoundaryPosition boundary => BoundaryRect(geometry, boundary),
        _ => null,
    };

    /// <summary>A boundary: its virtual paragraph, else the edge between the blocks around it.</summary>
    private Rect? BoundaryRect(BlockGeometry geometry, BoundaryPosition boundary)
    {
        if (geometry.Gap(boundary.Parent, boundary.Index) is { } gap)
        {
            var bounds = geometry.Bounds(gap);
            return new Rect(bounds.X, bounds.Y, 0, bounds.Height);
        }

        if (DocumentLookup.Find(_model(), boundary.Parent) is not ContainerBlock container)
        {
            return null;
        }

        double line = geometry.Document.EffectiveStyleSheet.FontSize * 1.3;
        foreach (int index in new[] { boundary.Index, boundary.Index - 1 })
        {
            if (index < 0 || index >= container.Children.Count)
            {
                continue;
            }

            var neighbour = container.Children[index].Id;
            if (geometry.Find(neighbour) is null && DocumentLookup.TopLevelIndex(_model(), neighbour) is { } item)
            {
                IncludeItem(geometry, item);
            }

            if (geometry.Find(neighbour) is { } view)
            {
                var bounds = geometry.Bounds(view);
                return index == boundary.Index ? new Rect(bounds.X, bounds.Y - line, 0, line) : new Rect(bounds.X, bounds.Bottom, 0, line);
            }
        }

        return null;
    }

    private static Rect FallbackRect(BlockGeometry geometry)
    {
        var viewport = Viewport(geometry);
        return new Rect(viewport.X, viewport.Y, 0, 0);
    }

    /// <summary>The visual line holding a position (its full height), or the caret rectangle at a boundary.</summary>
    private Rect LineBounds(BlockGeometry geometry, Position position)
    {
        if (position is TextPosition text && Locate(geometry, text) is { } located)
        {
            int index = located.Layout.LineIndexOfCaret(located.Display, located.Backward);
            var line = located.Layout.Line(index);
            var origin = geometry.TextOrigin(located.View);
            return new Rect(origin.X, origin.Y + line.Top, located.Layout.Width, line.Height);
        }

        return CaretRect(geometry, position) ?? FallbackRect(geometry);
    }

    private (MdTextBlockView View, Text.ITextBlockLayout Layout, BlockPositionMap Map, int Display, bool Backward)? Locate(BlockGeometry geometry, TextPosition position)
    {
        if (_projection()?.Map[position.Block] is not { } map)
        {
            return null;
        }

        if (geometry.Find(position.Block) is null && DocumentLookup.TopLevelIndex(_model(), position.Block) is { } item)
        {
            IncludeItem(geometry, item);
        }

        if (geometry.Find(position.Block) is not MdTextBlockView { TextLayout: { } layout } view)
        {
            return null;
        }

        return (view, layout, map, map.ToDisplay(position.Offset, position.Affinity), position.Affinity == Affinity.Backward);
    }

    /// <summary>Text lines and virtual paragraphs: what the caret can stand on.</summary>
    private static IEnumerable<Candidate> Candidates(BlockGeometry geometry)
    {
        foreach (var line in geometry.Lines())
        {
            yield return new Candidate(line.Bounds, line.View, null);
        }

        foreach (var gap in geometry.Realized.OfType<MdVirtualParagraph>())
        {
            yield return new Candidate(geometry.Bounds(gap), null, gap);
        }
    }

    private Position? HitTest(BlockGeometry geometry, Point point)
    {
        var candidates = Candidates(geometry).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        var row = candidates.Where(box => point.Y >= box.Bounds.Y && point.Y < box.Bounds.Bottom).ToList();
        if (row.Count == 0)
        {
            // Between blocks: the nearest box above or below.
            double Distance(Candidate box) => point.Y < box.Bounds.Y ? box.Bounds.Y - point.Y : point.Y - box.Bounds.Bottom;
            double nearest = candidates.Min(Distance);
            row = candidates.Where(box => Distance(box) <= nearest + Tolerance).ToList();
        }

        return Resolve(geometry, Nearest(row, point.X), point);
    }

    private Position? Resolve(BlockGeometry geometry, Candidate target, Point point)
    {
        if (target.Gap is { } gap)
        {
            return BlockGeometry.ParseKey(gap.BlockId) is { } parent ? new BoundaryPosition(parent, gap.Number) : null;
        }

        var view = target.View!;
        if (view.TextLayout is not { } layout || BlockGeometry.ParseKey(view.BlockId) is not { } block || _projection()?.Map[block] is not { } map)
        {
            return null;
        }

        var origin = geometry.TextOrigin(view);
        float y = (float)Math.Clamp(point.Y - origin.Y, target.Bounds.Y - origin.Y, target.Bounds.Bottom - origin.Y - 0.01);
        var (display, wrapped) = layout.HitTestLine(new Vector2((float)(point.X - origin.X), y));
        var affinity = wrapped ? Affinity.Backward : Affinity.Forward;
        return new TextPosition(block, map.ToSource(display, affinity), affinity);
    }

    private readonly record struct Candidate(Rect Bounds, MdTextBlockView? View, MdVirtualParagraph? Gap);
}
