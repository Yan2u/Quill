using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A block that holds blocks: quotes, lists, list items, tables, rows, definitions. By default it
/// stacks its children vertically inside <see cref="ContentInset"/>, separated by
/// <see cref="ChildSpacing"/> (by default the collapsed margins of the two blocks); it may draw decorations (bars, markers, borders) on its canvas behind them.
/// </summary>
public abstract partial class MdContainerView : MdBlockView
{
    /// <summary>The selection shows on the text (on the children for containers), not over the whole block (Q84).</summary>
    protected override bool ShowsWholeSelection => false;

    private readonly List<(MdBlockView Block, Rect Bounds)> _arranged = [];
    private bool _isActive;

    protected MdContainerView(bool draws)
        : base(draws)
    {
        Blocks = new BlockChildren(Children, () => InternalChildCount + _decorations);
    }

    private int _decorations;

    /// <summary>Adds a control of the container's own (not a block), before the blocks; the container lays it out.</summary>
    protected void AddDecoration(UIElement decoration) => Children.Insert(InternalChildCount + _decorations++, decoration);

    protected void RemoveDecoration(UIElement decoration)
    {
        if (Children.IndexOf(decoration) is int index and >= 0)
        {
            Children.RemoveAt(index);
            _decorations--;
        }
    }

    /// <summary>The child blocks, the content of the VNode.</summary>
    public IList<UIElement> Blocks { get; }

    /// <summary>The focus lies inside (tables and definitions are active then, 10).</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive != value)
            {
                _isActive = value;
                OnActiveChanged();
            }
        }
    }

    public override double FirstBaseline =>
        Blocks.Count > 0 && Blocks[0] is MdBlockView first ? ContentInset.Top + first.FirstBaseline : base.FirstBaseline;

    protected virtual Thickness ContentInset => default;

    /// <summary>The space between two adjacent children, the upper first.</summary>
    protected virtual double ChildSpacing(UIElement upper, UIElement lower) => Layout.BlockMargins.Between(Sheet, upper.GetType().Name, lower.GetType().Name);

    protected virtual void OnActiveChanged() => Redraw();

    protected override Size MeasureBlock(Size availableSize)
    {
        var inset = ContentInset;
        double width = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - inset.Left - inset.Right);
        double height = 0;
        double widest = 0;
        for (int i = 0; i < Blocks.Count; i++)
        {
            var child = Blocks[i];
            child.Measure(new Size(width, double.PositiveInfinity));
            height += child.DesiredSize.Height + (i > 0 ? ChildSpacing(Blocks[i - 1], child) : 0);
            widest = Math.Max(widest, child.DesiredSize.Width);
        }

        double outer = double.IsInfinity(availableSize.Width) ? widest + inset.Left + inset.Right : availableSize.Width;
        return new Size(outer, height + inset.Top + inset.Bottom);
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        ResetArrangedChildren();
        var inset = ContentInset;
        double width = Math.Max(0, finalSize.Width - inset.Left - inset.Right);
        double y = inset.Top;
        for (int i = 0; i < Blocks.Count; i++)
        {
            var child = Blocks[i];
            y += i > 0 ? ChildSpacing(Blocks[i - 1], child) : 0;
            ArrangeBlockChild(child, new Rect(inset.Left, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }

        if (HasOwnCanvas)
        {
            Redraw();
        }
    }

    protected void ResetArrangedChildren() => _arranged.Clear();

    protected void ArrangeBlockChild(UIElement child, Rect rect)
    {
        child.Arrange(rect);
        if (child is MdBlockView block)
        {
            _arranged.Add((block, rect));
        }
    }

    internal bool TryGetArrangedChildBounds(MdBlockView child, out Rect bounds)
    {
        foreach (var (block, rect) in _arranged)
        {
            if (ReferenceEquals(block, child))
            {
                bounds = rect;
                return true;
            }
        }

        bounds = default;
        return false;
    }

    protected override void DrawDelegatedChildren(CanvasDrawingSession session, Rect region)
    {
        Matrix3x2 baseTransform = session.Transform;
        foreach (var (block, rect) in _arranged)
        {
            if (block.HasOwnCanvas || block.Opacity <= 0 || block.Visibility != Visibility.Visible || rect.Width <= 0 || rect.Height <= 0)
            {
                continue;
            }

            if (rect.Bottom < region.Top - 2 || rect.Top > region.Bottom + 2
                || rect.Right < region.Left - 2 || rect.Left > region.Right + 2)
            {
                continue;
            }

            double clipLeft = Math.Max(region.Left, rect.Left) - rect.X;
            double clipTop = Math.Max(region.Top, rect.Top) - rect.Y;
            double clipRight = Math.Min(region.Right, rect.Right) - rect.X;
            double clipBottom = Math.Min(region.Bottom, rect.Bottom) - rect.Y;
            Rect localRegion = clipRight > clipLeft && clipBottom > clipTop
                ? new Rect(clipLeft, clipTop, clipRight - clipLeft, clipBottom - clipTop)
                : new Rect(0, 0, rect.Width, rect.Height);

            if (block.Context.Surface != Context.Surface)
            {
                session.FillRectangle(rect, block.Context.Surface);
            }

            try
            {
                session.Transform = Matrix3x2.CreateTranslation((float)rect.X, (float)rect.Y) * baseTransform;
                block.DrawDelegated(session, localRegion);
            }
            finally
            {
                session.Transform = baseTransform;
            }
        }
    }

    internal override void InvalidateDelegatedChild(MdBlockView child, Rect? childRegion)
    {
        if (!HasOwnCanvas)
        {
            base.InvalidateDelegatedChild(child, childRegion);
            return;
        }

        if (TryResolveChildRect(child, childRegion, out Rect mapped))
        {
            InvalidateRegion(new Rect(Math.Max(0, mapped.Left - 1), Math.Max(0, mapped.Top - 1), mapped.Width + 2, mapped.Height + 2));
            return;
        }

        Redraw();
    }

    private bool TryResolveChildRect(MdBlockView child, Rect? childRegion, out Rect mapped)
    {
        static DependencyObject? ParentOf(DependencyObject element) =>
            VisualTreeHelper.GetParent(element) ?? (element as FrameworkElement)?.Parent;

        Rect local = childRegion ?? new Rect(0, 0, child.EffectiveWidth, child.EffectiveHeight);
        if (local.Width <= 0 || local.Height <= 0)
        {
            mapped = default;
            return false;
        }

        double x = local.X;
        double y = local.Y;
        if (ReferenceEquals(child, this))
        {
            mapped = local;
            return true;
        }

        MdBlockView current = child;
        for (DependencyObject? parent = ParentOf(child); parent is not null; parent = ParentOf(parent))
        {
            if (parent is not MdContainerView container || !container.TryGetArrangedChildBounds(current, out Rect childBounds))
            {
                break;
            }

            x += childBounds.X;
            y += childBounds.Y;
            if (ReferenceEquals(container, this))
            {
                mapped = new Rect(x, y, local.Width, local.Height);
                return true;
            }

            current = container;
        }

        mapped = default;
        return false;
    }

    protected override void OnBlockPooled()
    {
        base.OnBlockPooled();
        _arranged.Clear();
    }

    protected override void OnBlockUnmounted()
    {
        base.OnBlockUnmounted();
        _arranged.Clear();
    }
}
