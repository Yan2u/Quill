using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A table row: its cells side by side at the column widths the table decided, one pixel apart, all
/// as tall as the tallest. The row gives its cells their background: the header's, the stripe's on
/// every second body row, or the body cells'.
/// </summary>
public sealed partial class MdTableRow : MdContainerView
{
    private const double Border = 1;

    private bool _isHeader;
    private bool _isStriped;

    public MdTableRow()
        : base(draws: false)
    {
    }

    public bool IsHeader
    {
        get => _isHeader;
        set => SetAndRemeasure(ref _isHeader, value);
    }

    /// <summary>An even body row (the second, the fourth…); set by the table before it measures the row.</summary>
    internal bool IsStriped
    {
        get => _isStriped;
        set
        {
            if (_isStriped != value)
            {
                _isStriped = value;
                foreach (var cell in Blocks.OfType<MdBlockView>())
                {
                    cell.InvalidateStyle();
                }
            }
        }
    }

    /// <summary>Set by the table before it measures the row.</summary>
    internal IReadOnlyList<double> ColumnWidths { get; set; } = [];

    protected internal override BlockContext ChildContext => Context with
    {
        Surface = _isHeader ? Sheet.TableHeaderBackground
            : _isStriped && Sheet.TableStripeBackground is { } stripe ? stripe
            : Sheet.TableCellBackground ?? Context.Surface,
    };

    public override double FirstBaseline => Blocks.Count > 0 && Blocks[0] is MdBlockView first ? first.FirstBaseline : base.FirstBaseline;

    protected override Size MeasureBlock(Size availableSize)
    {
        double height = 0;
        double width = 0;
        for (int i = 0; i < Blocks.Count; i++)
        {
            double column = i < ColumnWidths.Count ? ColumnWidths[i] : 0;
            Blocks[i].Measure(new Size(column, double.PositiveInfinity));
            height = Math.Max(height, Blocks[i].DesiredSize.Height);
            width += column + (i > 0 ? Border : 0);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? width : availableSize.Width, height);
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        double x = 0;
        for (int i = 0; i < Blocks.Count; i++)
        {
            double column = i < ColumnWidths.Count ? ColumnWidths[i] : 0;
            Blocks[i].Arrange(new Rect(x, 0, column, finalSize.Height));
            x += column + Border;
        }
    }
}
