using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Elements;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>A visual line of a realized text block, in page coordinates.</summary>
internal readonly record struct LineBox(MdTextBlockView View, int Index, Rect Bounds);

/// <summary>
/// The realized block views of a document and their geometry in page coordinates (the scrolled
/// content of <see cref="MdDocument"/>, so independent of the scroll offset). Views the virtual
/// stack recycled stay in the tree, far off; they are skipped. Blocks laid out outside the tree
/// (Q71) can be included with their place in the page; queries then treat them like realized ones.
/// The index of realized views is kept until the set of realized views can have changed (a reconcile,
/// or the virtual stack preparing, clearing or moving an element: <see cref="Invalidate"/>); bounds
/// are computed on demand, so layout passes that only move views keep it (09).
/// </summary>
internal sealed class BlockGeometry
{
    private readonly MdDocument _document;
    private Dictionary<string, MdBlockView>? _views;
    private List<MdBlockView>? _ordered;
    private readonly Dictionary<MdBlockView, (MdBlockView Root, Point Origin)> _detached = [];
    private readonly Dictionary<string, MdBlockView> _detachedViews = [];
    private readonly List<MdBlockView> _detachedOrder = [];
    private List<MdBlockView>? _withDetached;

    public BlockGeometry(MdDocument document)
    {
        _document = document;
    }

    public MdDocument Document => _document;

    /// <summary>The realized blocks in document order.</summary>
    public IReadOnlyList<MdBlockView> Realized
    {
        get
        {
            Build();
            return _detached.Count == 0 ? _ordered! : _withDetached ??= [.. _ordered!, .. _detachedOrder];
        }
    }

    /// <summary>How many times the index was rebuilt, for measurements.</summary>
    public int Builds { get; private set; }

    /// <summary>The realized views may have changed: rebuild the index on the next query.</summary>
    public void Invalidate()
    {
        _views = null;
        _ordered = null;
        ClearDetached();
    }

    /// <summary>Forgets the blocks included from outside the tree; they are laid out again per query.</summary>
    public void ClearDetached()
    {
        if (_detached.Count == 0)
        {
            return;
        }

        _detached.Clear();
        _detachedViews.Clear();
        _detachedOrder.Clear();
        _withDetached = null;
    }

    /// <summary>Includes a detached, laid out block tree whose top-left lies at <paramref name="origin"/> in the page.</summary>
    public void Include(MdBlockView root, Point origin)
    {
        Build();
        Walk(root);

        void Walk(MdBlockView view)
        {
            if (_detached.ContainsKey(view))
            {
                return;
            }

            _detached[view] = (root, origin);
            if (view.BlockId.Length > 0 && view is not MdVirtualParagraph && !_views!.ContainsKey(view.BlockId))
            {
                _detachedViews.TryAdd(view.BlockId, view);
            }

            _detachedOrder.Add(view);
            _withDetached = null;
            foreach (var child in view.Children.OfType<MdBlockView>())
            {
                Walk(child);
            }
        }
    }

    public bool IsDetached(MdBlockView view) => _detached.ContainsKey(view);

    public MdBlockView? Find(BlockId id) => Find(DocumentProjector.KeyOf(id));

    public MdBlockView? Find(string key)
    {
        Build();
        return _views!.GetValueOrDefault(key) ?? _detachedViews.GetValueOrDefault(key);
    }

    /// <summary>The bounds of a view in page coordinates.</summary>
    public Rect Bounds(FrameworkElement view)
    {
        if (view is MdBlockView block && _detached.TryGetValue(block, out var detached))
        {
            // Outside the tree: the arrange slots up to the detached root, placed at its origin.
            var slot = LayoutInformation.GetLayoutSlot(view);
            double x = detached.Origin.X;
            double y = detached.Origin.Y;
            for (FrameworkElement? element = view; element is not null && !ReferenceEquals(element, detached.Root); element = element.Parent as FrameworkElement)
            {
                var placed = LayoutInformation.GetLayoutSlot(element);
                x += placed.X;
                y += placed.Y;
            }

            return new Rect(x, y, slot.Width, slot.Height);
        }

        return view.TransformToVisual(_document.Page).TransformBounds(new Rect(0, 0, view.ActualWidth, view.ActualHeight));
    }

    /// <summary>The caret rectangle at a display offset of a realized text block, or null.</summary>
    public Rect? CaretRect(BlockId leaf, int display, bool backward)
    {
        if (Find(leaf) is not MdTextBlockView { TextLayout: { } layout } view)
        {
            return null;
        }

        var origin = TextOrigin(view);
        var caret = layout.CaretRect(display, backward);
        return new Rect(origin.X + caret.X, origin.Y + caret.Y, caret.Width, caret.Height);
    }

    /// <summary>Where a text block's text starts, in page coordinates.</summary>
    public Point TextOrigin(MdTextBlockView view)
    {
        var bounds = Bounds(view);
        var padding = view.TextPadding;
        return new Point(bounds.X + padding.Left - view.TextScrollOffset, bounds.Y + padding.Top);
    }

    /// <summary>Every visual line of the realized text blocks.</summary>
    public IEnumerable<LineBox> Lines()
    {
        foreach (var block in Realized)
        {
            if (block is not MdTextBlockView { TextLayout: { } layout } view)
            {
                continue;
            }

            var bounds = Bounds(view);
            var padding = view.TextPadding;
            for (int i = 0; i < layout.LineCount; i++)
            {
                var line = layout.Line(i);
                yield return new LineBox(view, i, new Rect(bounds.X, bounds.Y + padding.Top + line.Top, bounds.Width, line.Height));
            }
        }
    }

    /// <summary>The realized virtual paragraph at a boundary, or null.</summary>
    public MdVirtualParagraph? Gap(BlockId parent, int index)
    {
        string key = DocumentProjector.KeyOf(parent);
        return Realized.OfType<MdVirtualParagraph>().FirstOrDefault(gap => gap.BlockId == key && gap.Number == index);
    }

    /// <summary>The block ID behind a key (<c>b42</c>).</summary>
    public static BlockId? ParseKey(string key) =>
        key.Length > 1 && key[0] == 'b' && long.TryParse(key.AsSpan(1), out long value) ? new BlockId(value) : null;

    private void Build()
    {
        if (_views is not null)
        {
            return;
        }

        _views = [];
        _ordered = [];
        _withDetached = null;
        Builds++;
        Collect(_document.Page);
    }

    private void Collect(DependencyObject element)
    {
        int count = VisualTreeHelper.GetChildrenCount(element);
        var children = Enumerable.Range(0, count).Select(i => VisualTreeHelper.GetChild(element, i));
        if (element is ItemsRepeater repeater)
        {
            // The repeater keeps recycled elements as children and does not order them: by item index.
            children = children.OfType<UIElement>()
                .Select(item => (Item: item, Index: repeater.GetElementIndex(item)))
                .Where(pair => pair.Index >= 0)
                .OrderBy(pair => pair.Index)
                .Select(pair => (DependencyObject)pair.Item)
                .ToList();
        }

        foreach (var child in children)
        {
            if (child is MdBlockView block)
            {
                // A virtual paragraph carries its container's key; it is found through Gap.
                if (block.BlockId.Length > 0 && block is not MdVirtualParagraph)
                {
                    _views![block.BlockId] = block;
                }

                _ordered!.Add(block);
            }

            Collect(child);
        }
    }
}
