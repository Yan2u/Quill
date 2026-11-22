using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The toolbar over the active table (10, Q76–Q78): icons only, in the Windows 11 style (subtle
/// buttons with Segoe Fluent Icons glyphs and tooltips). Left: resize, align left, center, right;
/// right: more operations, delete table. Buttons never take the focus, so the caret stays in the
/// table; every action is reported as a command for the editor to run.
/// </summary>
internal sealed partial class TableToolbar : Grid
{
    private const double ButtonSize = 22;

    private readonly TableResizeGrid _resize = new();
    private readonly Flyout _resizeFlyout;
    private readonly Button _resizeButton;
    private readonly Button _alignLeft;
    private readonly Button _alignCenter;
    private readonly Button _alignRight;
    private readonly Button _more;
    private readonly Button _delete;

    public TableToolbar()
    {
        Height = ButtonSize;
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _resizeFlyout = new Flyout { Content = _resize, Placement = FlyoutPlacementMode.BottomEdgeAlignedLeft };
        _resize.Resized += (_, size) =>
        {
            Run(new ResizeTable(size.Columns, size.Rows));
            _resize.Show(size.Columns, size.Rows);
        };

        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _resizeButton = Icon("", UiStrings.Get("table.toolbar.resize"));
        _resizeButton.Click += (_, _) =>
        {
            _resize.Show(Columns, Rows);
            _resizeFlyout.ShowAt(_resizeButton);
        };
        _alignLeft = Command("", UiStrings.Get("table.toolbar.alignLeft"), new SetColumnAlignment(TableAlignment.Left));
        _alignCenter = Command("", UiStrings.Get("table.toolbar.alignCenter"), new SetColumnAlignment(TableAlignment.Center));
        _alignRight = Command("", UiStrings.Get("table.toolbar.alignRight"), new SetColumnAlignment(TableAlignment.Right));
        left.Children.Add(_resizeButton);
        left.Children.Add(_alignLeft);
        left.Children.Add(_alignCenter);
        left.Children.Add(_alignRight);
        Children.Add(left);

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        _more = Icon("", UiStrings.Get("table.toolbar.more"));
        _delete = Command("", UiStrings.Get("table.toolbar.deleteTable"), new EditTable(TableEdit.DeleteTable));
        right.Children.Add(_more);
        right.Children.Add(_delete);
        SetColumn(right, 2);
        Children.Add(right);
        ApplyLanguage();
    }

    /// <summary>A toolbar action: a command for the editor, or a copy of the table.</summary>
    public event EventHandler<Command?>? Action;

    /// <summary>The size of the table shown, for the resize flyout.</summary>
    public int Columns { get; set; } = 1;

    public int Rows { get; set; } = 1;

    public TableResizeGrid ResizeGrid => _resize;

    /// <summary>The height the toolbar takes above the table.</summary>
    public static double BarHeight => ButtonSize;

    private static Button Icon(string glyph, string tip)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = 12, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons,Segoe MDL2 Assets") },
            Width = ButtonSize,
            Height = ButtonSize,
            Padding = new Thickness(0),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            AllowFocusOnInteraction = false,
            IsTabStop = false,
        };
        ToolTipService.SetToolTip(button, tip);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, tip);
        return button;
    }

    private Button Command(string glyph, string tip, Command command)
    {
        var button = Icon(glyph, tip);
        button.Click += (_, _) => Run(command);
        return button;
    }

    public void ApplyLanguage()
    {
        SetTip(_resizeButton, "table.toolbar.resize");
        SetTip(_alignLeft, "table.toolbar.alignLeft");
        SetTip(_alignCenter, "table.toolbar.alignCenter");
        SetTip(_alignRight, "table.toolbar.alignRight");
        SetTip(_more, "table.toolbar.more");
        SetTip(_delete, "table.toolbar.deleteTable");
        _resize.ApplyLanguage();
        _more.Flyout = MoreMenu();
    }

    private static void SetTip(Button button, string key)
    {
        string tip = UiStrings.Get(key);
        ToolTipService.SetToolTip(button, tip);
        AutomationProperties.SetName(button, tip);
    }

    /// <summary>The five groups of 10: insert, move, delete rows and columns; copy and format; delete the table.</summary>
    private MenuFlyout MoreMenu()
    {
        var menu = new MenuFlyout();
        void Item(string text, Command? command, string? shortcut = null)
        {
            var item = new MenuFlyoutItem { Text = text };
            if (shortcut is not null)
            {
                item.KeyboardAcceleratorTextOverride = shortcut;
            }

            item.Click += (_, _) => Run(command);
            menu.Items.Add(item);
        }

        void Separator() => menu.Items.Add(new MenuFlyoutSeparator());

        Item(UiStrings.Get("menu.row-above"), new EditTable(TableEdit.InsertRowAbove));
        Item(UiStrings.Get("menu.row-below"), new EditTable(TableEdit.InsertRowBelow), "Ctrl+Enter");
        Item(UiStrings.Get("menu.column-left"), new EditTable(TableEdit.InsertColumnLeft));
        Item(UiStrings.Get("menu.column-right"), new EditTable(TableEdit.InsertColumnRight));
        Separator();
        Item(UiStrings.Get("menu.row-up"), new EditTable(TableEdit.MoveRowUp), "Alt+Up");
        Item(UiStrings.Get("menu.row-down"), new EditTable(TableEdit.MoveRowDown), "Alt+Down");
        Item(UiStrings.Get("menu.column-left-move"), new EditTable(TableEdit.MoveColumnLeft), "Alt+Left");
        Item(UiStrings.Get("menu.column-right-move"), new EditTable(TableEdit.MoveColumnRight), "Alt+Right");
        Separator();
        Item(UiStrings.Get("menu.delete-row"), new EditTable(TableEdit.DeleteRow), "Ctrl+Shift+Backspace");
        Item(UiStrings.Get("menu.delete-column"), new EditTable(TableEdit.DeleteColumn));
        Separator();
        Item(UiStrings.Get("table.toolbar.copyTable"), null);
        Item(UiStrings.Get("menu.format-table"), new EditTable(TableEdit.FormatSource));
        Separator();
        Item(UiStrings.Get("menu.delete-table"), new EditTable(TableEdit.DeleteTable));
        return menu;
    }

    private void Run(Command? command) => Action?.Invoke(this, command);
}
