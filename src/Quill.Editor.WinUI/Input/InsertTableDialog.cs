using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Windows.System;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The Ctrl+T dialog (layout after Typora's, look of Windows 11): columns and rows side by side, then
/// Cancel and the accent Insert button. Enter in either box inserts; the rows count the header row〔默认〕.
/// </summary>
internal sealed partial class InsertTableDialog : ContentDialog
{
    /// <summary>The size offered when the dialog opens (Typora's dialog shows 3 × 4).</summary>
    public const int DefaultColumns = 3;

    public const int DefaultRows = 4;

    private readonly NumberBox _columns = Box(DefaultColumns, 64, UiStrings.Get("table.dialog.columns"));
    private readonly NumberBox _rows = Box(DefaultRows, 999, UiStrings.Get("table.dialog.rows"));
    private bool _accepted;

    public InsertTableDialog()
    {
        Title = UiStrings.Get("table.dialog.title");
        PrimaryButtonText = UiStrings.Get("table.dialog.insert");
        CloseButtonText = UiStrings.Get("table.dialog.cancel");
        DefaultButton = ContentDialogButton.Primary;
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        fields.Children.Add(_columns);
        fields.Children.Add(_rows);
        Content = fields;
        PrimaryButtonClick += (_, _) => _accepted = true;
        foreach (var box in new[] { _columns, _rows })
        {
            box.KeyDown += (_, args) =>
            {
                if (args.Key == VirtualKey.Enter)
                {
                    args.Handled = true;
                    _accepted = true;
                    Hide();
                }
            };
        }

        Opened += (_, _) => _columns.Focus(FocusState.Programmatic);
    }

    /// <summary>The size entered, or null when a box is empty.</summary>
    public (int Columns, int Rows)? Size =>
        double.IsNaN(_columns.Value) || double.IsNaN(_rows.Value) ? null : ((int)_columns.Value, (int)_rows.Value);

    /// <summary>Shows the dialog; the size chosen, or null when cancelled.</summary>
    public async Task<(int Columns, int Rows)?> AskAsync()
    {
        _accepted = false;
        await ShowAsync();
        return _accepted ? Size : null;
    }

    internal void SetSize(int columns, int rows)
    {
        _columns.Value = columns;
        _rows.Value = rows;
    }

    private static NumberBox Box(int value, int maximum, string header) => new()
    {
        Header = header,
        Value = value,
        Minimum = 1,
        Maximum = maximum,
        SmallChange = 1,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
        ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        Width = 140,
    };
}
