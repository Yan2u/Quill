using Microsoft.UI.Xaml;
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
        var inset = ContentInset;
        double width = Math.Max(0, finalSize.Width - inset.Left - inset.Right);
        double y = inset.Top;
        for (int i = 0; i < Blocks.Count; i++)
        {
            var child = Blocks[i];
            y += i > 0 ? ChildSpacing(Blocks[i - 1], child) : 0;
            child.Arrange(new Rect(inset.Left, y, width, child.DesiredSize.Height));
            y += child.DesiredSize.Height;
        }
    }
}
