using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;
using Windows.System;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The attached nodes that are pure view (05): the <c>H1</c>…<c>H6</c> badge left of the focused
/// heading (10), the hover flyout (Q29), the table toolbar and the inline math preview. They lie over
/// the content and never take part in its layout, so they cause no reflow. (The language of a code block
/// is document text since Q91, drawn by <see cref="MdCodeLanguage"/>.)
/// </summary>
internal sealed partial class AdornerLayer : Canvas
{
    private const double BadgeGap = 8;
    private const double HoverGap = 4;

    private readonly TextBlock _badge = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly TextBlock _hoverText = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 360 };
    private readonly Border _hover;
    private readonly TableToolbar _toolbar = new();
    private readonly Border _toolbarHost;
    private MdStyleSheet? _sheet;
    private bool? _warning;

    public AdornerLayer()
    {
        _hover = new Border
        {
            Child = _hoverText,
            Padding = new Thickness(8, 4, 8, 4),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            IsHitTestVisible = false,
            Visibility = Visibility.Collapsed,
        };
        _toolbarHost = new Border
        {
            Child = _toolbar,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1),
            Visibility = Visibility.Collapsed,
        };
        _toolbar.Action += OnToolbarAction;
        Children.Add(_toolbarHost);
        Children.Add(_badge);
        Children.Add(_hover);
    }

    /// <summary>A table toolbar action: a command to run, or null to copy the table (Q77).</summary>
    public event EventHandler<Commands.Command?>? TableAction;

    internal void Release()
    {
        ReleaseMathPreview();
        _toolbar.Action -= OnToolbarAction;
        TableAction = null;
    }

    internal void RefreshLanguage() => _toolbar.ApplyLanguage();

    private void OnToolbarAction(object? sender, Commands.Command? command) => TableAction?.Invoke(this, command);

    /// <summary>The table toolbar as shown, for the probes: the table's key and the bar's bounds in page coordinates.</summary>
    public (string TableKey, Rect Bounds)? Toolbar { get; private set; }

    internal TableToolbar TableToolbar => _toolbar;

    /// <summary>The heading badge as shown, for the probes: its text and bounds in page coordinates.</summary>
    public (string Text, Rect Bounds)? Badge { get; private set; }

    /// <summary>The hover flyout as shown, for the probes: its text and bounds in page coordinates.</summary>
    public (string Text, Rect Bounds)? Flyout { get; private set; }

    public void Update(BlockGeometry geometry, HoverInfo? hover, MdStyleSheet sheet)
    {
        // Runs after every layout pass: assign only what changed, or the assignments themselves
        // would start another pass, and another (a layout cycle).
        if (!ReferenceEquals(sheet, _sheet))
        {
            _sheet = sheet;
            _warning = null;
            _badge.FontSize = sheet.FontSize * 0.7;
            _badge.Foreground = new SolidColorBrush(sheet.SecondaryForeground);
            _hover.Background = new SolidColorBrush(sheet.PageBackground);
            _hover.BorderBrush = new SolidColorBrush(sheet.ObjectBorder);
            _toolbarHost.Background = new SolidColorBrush(sheet.PageBackground);
            _toolbarHost.BorderBrush = new SolidColorBrush(sheet.ObjectBorder);
        }

        var origin = TransformToVisual(geometry.Document.Page).TransformPoint(default);
        var realized = geometry.Realized.Where(block => !geometry.IsDetached(block)).ToList();
        UpdateBadge(geometry, realized.OfType<MdHeading>().FirstOrDefault(static heading => heading.IsFocused), sheet, origin);
        UpdateHover(geometry, hover, sheet, origin);
        UpdateMathPreview(geometry, realized, sheet, origin);
        UpdateToolbar(geometry, realized.OfType<MdTable>().FirstOrDefault(static table => table.IsActive), origin);
    }

    private static void Place(FrameworkElement element, double x, double y)
    {
        if (Math.Abs(GetLeft(element) - x) > 0.01)
        {
            SetLeft(element, x);
        }

        if (Math.Abs(GetTop(element) - y) > 0.01)
        {
            SetTop(element, y);
        }

        Show(element, true);
    }

    private static void Show(UIElement element, bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (element.Visibility != visibility)
        {
            element.Visibility = visibility;
        }
    }

    private static void SetText(TextBlock block, string text)
    {
        if (block.Text != text)
        {
            block.Text = text;
        }
    }

    private void UpdateBadge(BlockGeometry geometry, MdHeading? heading, MdStyleSheet sheet, Point origin)
    {
        if (heading is null)
        {
            Show(_badge, false);
            Badge = null;
            return;
        }

        SetText(_badge, $"H{heading.Level}");

        // Left of the heading's first line, in the page margin: the heading's text does not move (10).
        var bounds = geometry.Bounds(heading);
        double x = bounds.X - BadgeGap - _badge.DesiredSize.Width;
        double y = bounds.Y + heading.FirstBaseline - _badge.FontSize * 1.05;
        Place(_badge, x - origin.X, y - origin.Y);
        Badge = (_badge.Text, new Rect(x, y, _badge.DesiredSize.Width, _badge.DesiredSize.Height));
    }

    private void UpdateHover(BlockGeometry geometry, HoverInfo? hover, MdStyleSheet sheet, Point origin)
    {
        if (hover is null || geometry.Find(hover.BlockKey) is not MdTextBlockView { TextLayout: { } layout } view)
        {
            Show(_hover, false);
            Flyout = null;
            return;
        }

        var regions = layout.Regions(hover.DisplayStart, hover.DisplayEnd);
        if (regions.Count == 0)
        {
            Show(_hover, false);
            Flyout = null;
            return;
        }

        var text = geometry.TextOrigin(view);
        double left = text.X + regions.Min(static region => region.X);
        double bottom = text.Y + regions.Max(static region => region.Bottom);
        string shownText = LocalizeHoverText(hover);
        SetText(_hoverText, shownText);
        if (_warning != hover.IsWarning)
        {
            _warning = hover.IsWarning;
            _hoverText.Foreground = new SolidColorBrush(hover.IsWarning ? Microsoft.UI.Colors.DarkOrange : sheet.Foreground);
        }

        Place(_hover, left - origin.X, bottom + HoverGap - origin.Y);
        Flyout = (shownText, new Rect(left, bottom + HoverGap, _hover.DesiredSize.Width, _hover.DesiredSize.Height));
    }

    private static string LocalizeHoverText(HoverInfo hover)
    {
        const string prefix = "Footnote [^";
        const string suffix = "] is not defined.";
        if (hover.IsWarning && hover.Text.StartsWith(prefix, StringComparison.Ordinal) && hover.Text.EndsWith(suffix, StringComparison.Ordinal))
        {
            string label = hover.Text[prefix.Length..^suffix.Length];
            return UiStrings.Format("editor.footnoteNotDefined", label);
        }

        return hover.Text;
    }

    /// <summary>
    /// Over the active table, in the block spacing above it (10, Q76): as wide as the table, at least
    /// wide enough for its buttons; it covers what is above rather than moving anything.
    /// </summary>
    private void UpdateToolbar(BlockGeometry geometry, MdTable? table, Point origin)
    {
        if (table is null)
        {
            Show(_toolbarHost, false);
            Toolbar = null;
            return;
        }

        _toolbar.Columns = Math.Max(1, table.Alignments.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
        _toolbar.Rows = Math.Max(1, table.Blocks.Count);
        var bounds = geometry.Bounds(table);
        double width = Math.Max(220, table.GridWidth);
        if (double.IsNaN(_toolbarHost.Width) || Math.Abs(_toolbarHost.Width - width) > 0.5)
        {
            _toolbarHost.Width = width;
        }

        double height = TableToolbar.BarHeight + 4;
        Place(_toolbarHost, bounds.X - origin.X, bounds.Y - height - origin.Y);
        Toolbar = (table.BlockId, new Rect(bounds.X, bounds.Y - height, width, height));
    }
}
