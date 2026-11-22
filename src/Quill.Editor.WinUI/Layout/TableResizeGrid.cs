using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The content of the table's resize flyout (Q78): small squares showing the table's size, the
/// header row darker; moving over them previews a size and a click applies it; two number boxes
/// below take a size typed in. The grid shows a little more than the table so it can grow. Colors
/// come from the Windows 11 theme resources. The preview stays as it is when the pointer is over a
/// gap between squares or leaves the grid, so moving across the grid does not flicker.
/// </summary>
internal sealed partial class TableResizeGrid : StackPanel
{
    private const double Cell = 14;
    private const double Gap = 3;

    // A transparent background makes the gaps part of the grid: a gap belongs to the square before it.
    private readonly Canvas _cells = new() { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    private readonly NumberBox _columns = new() { Minimum = 1, Maximum = 64, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, Width = 56 };
    private readonly NumberBox _rows = new() { Minimum = 1, Maximum = 999, SmallChange = 1, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden, Width = 56 };
    private readonly List<Rectangle> _squares = [];
    private int _gridColumns;
    private int _gridRows;
    private (int Columns, int Rows) _size;
    private (int Columns, int Rows) _shown;
    private bool _updating;

    public TableResizeGrid()
    {
        Spacing = 10;
        Children.Add(_cells);
        var inputs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        inputs.Children.Add(_columns);
        inputs.Children.Add(new TextBlock { Text = "×", VerticalAlignment = VerticalAlignment.Center });
        inputs.Children.Add(_rows);
        Children.Add(inputs);
        ToolTipService.SetToolTip(_columns, UiStrings.Get("table.dialog.columns"));
        ToolTipService.SetToolTip(_rows, UiStrings.Get("table.dialog.rows"));
        _columns.ValueChanged += (_, _) => Typed();
        _rows.ValueChanged += (_, _) => Typed();
        _cells.PointerMoved += (_, args) => Preview(Hit(args.GetCurrentPoint(_cells).Position));
        _cells.PointerPressed += (_, args) =>
        {
            var size = Hit(args.GetCurrentPoint(_cells).Position);
            args.Handled = true;
            Resized?.Invoke(this, size);
        };
    }

    public void ApplyLanguage()
    {
        ToolTipService.SetToolTip(_columns, UiStrings.Get("table.dialog.columns"));
        ToolTipService.SetToolTip(_rows, UiStrings.Get("table.dialog.rows"));
    }

    /// <summary>A size was chosen: columns and rows, the header row included.</summary>
    public event EventHandler<(int Columns, int Rows)>? Resized;

    /// <summary>Shows the table's current size.</summary>
    public void Show(int columns, int rows)
    {
        _size = (columns, rows);
        _gridColumns = Math.Max(6, columns + 2);
        _gridRows = Math.Max(10, rows + 2);
        _cells.Children.Clear();
        _squares.Clear();
        for (int row = 0; row < _gridRows; row++)
        {
            for (int column = 0; column < _gridColumns; column++)
            {
                var square = new Rectangle { Width = Cell, Height = Cell, RadiusX = 2, RadiusY = 2, StrokeThickness = 1 };
                Canvas.SetLeft(square, column * (Cell + Gap));
                Canvas.SetTop(square, row * (Cell + Gap));
                _cells.Children.Add(square);
                _squares.Add(square);
            }
        }

        _cells.Width = _gridColumns * (Cell + Gap) - Gap;
        _cells.Height = _gridRows * (Cell + Gap) - Gap;
        _shown = default;
        Preview(_size);
    }

    /// <summary>The size the squares show: the table's, or the last one previewed.</summary>
    internal (int Columns, int Rows) Shown => _shown;

    /// <summary>Previews the size under a point of the grid, as a pointer move does.</summary>
    internal void PreviewAt(Windows.Foundation.Point point) => Preview(Hit(point));

    private static Brush Theme(string key, Windows.UI.Color fallback) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush ? brush : new SolidColorBrush(fallback);

    private (int Columns, int Rows) Hit(Windows.Foundation.Point point) =>
        (Math.Clamp((int)(point.X / (Cell + Gap)) + 1, 1, _gridColumns), Math.Clamp((int)(point.Y / (Cell + Gap)) + 1, 1, _gridRows));

    private void Preview((int Columns, int Rows) size)
    {
        if (size == _shown)
        {
            return;
        }

        _shown = size;
        var header = Theme("AccentFillColorDefaultBrush", Microsoft.UI.Colors.SteelBlue);
        var body = Theme("AccentFillColorSecondaryBrush", Microsoft.UI.Colors.LightSteelBlue);
        var empty = Theme("ControlFillColorDefaultBrush", Microsoft.UI.Colors.WhiteSmoke);
        var stroke = Theme("ControlStrokeColorDefaultBrush", Microsoft.UI.Colors.Gainsboro);
        for (int index = 0; index < _squares.Count; index++)
        {
            int row = index / _gridColumns;
            int column = index % _gridColumns;
            bool inside = row < size.Rows && column < size.Columns;
            _squares[index].Fill = inside ? (row == 0 ? header : body) : empty;
            _squares[index].Stroke = stroke;
        }

        _updating = true;
        _columns.Value = size.Columns;
        _rows.Value = size.Rows;
        _updating = false;
    }

    private void Typed()
    {
        if (_updating || double.IsNaN(_columns.Value) || double.IsNaN(_rows.Value))
        {
            return;
        }

        var size = ((int)_columns.Value, (int)_rows.Value);
        if (size != _size)
        {
            Resized?.Invoke(this, size);
        }
    }
}
