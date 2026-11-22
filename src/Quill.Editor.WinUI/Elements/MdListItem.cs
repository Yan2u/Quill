using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Documents;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A list item: its marker (bullet by level, number, or task checkbox) in the indent, on the
/// baseline of the first line; its blocks indented. Blocks after the first are the headless items
/// of 02: same level, no marker. The task checkbox is a native <see cref="CheckBox"/> (theme, hover
/// and pressed looks of the system), scaled to the text; a click asks the editor to toggle the task
/// (a command), and the box shows the state the projection gives back.
/// </summary>
public sealed partial class MdListItem : MdContainerView
{
    private static readonly string[] Bullets = ["•", "◦", "▪"];

    /// <summary>The size of the native checkbox's box; its template is this wide and 32 tall, the box centered.</summary>
    private const double NativeBox = 20;
    private const double NativeHeight = 32;

    private int _level = 1;
    private int _number;
    private TaskState _task;
    private CanvasTextLayout? _marker;
    private double _drawnBaseline = -1;
    private CheckBox? _checkBox;
    private bool _syncing;

    public MdListItem()
        : base(draws: true)
    {
    }

    public int Level
    {
        get => _level;
        set => SetMarker(ref _level, value);
    }

    /// <summary>The displayed number in an ordered list; 0 in a bullet list.</summary>
    public int Number
    {
        get => _number;
        set => SetMarker(ref _number, value);
    }

    public TaskState Task
    {
        get => _task;
        set
        {
            SetMarker(ref _task, value);
            UpdateCheckBox();
        }
    }

    protected override Thickness ContentInset => new(Sheet.ListIndent, 0, 0, 0);

    protected override double ChildSpacing(UIElement upper, UIElement lower) => Sheet.ListItemSpacing;

    protected override void OnContextChanged() => DropMarker();

    protected override void OnBlockUnmounted()
    {
        base.OnBlockUnmounted();
        DropMarker();
        DetachCheckBox();
    }

    protected override Size MeasureBlock(Size availableSize)
    {
        var size = base.MeasureBlock(availableSize);
        _checkBox?.Measure(new Size(NativeBox, NativeHeight));
        return size;
    }

    protected override void ArrangeBlock(Size finalSize)
    {
        base.ArrangeBlock(finalSize);
        if (_checkBox is not null && CheckboxBounds is { } box)
        {
            // The template's box is centered in it: center the template on the box, then scale it to the text.
            double centerX = box.X + box.Width / 2;
            double centerY = box.Y + box.Height / 2;
            _checkBox.Arrange(new Rect(centerX - NativeBox / 2, centerY - NativeHeight / 2, NativeBox, NativeHeight));
            double scale = box.Width / NativeBox;
            _checkBox.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
        }

        // The marker follows the first line of the first block, which lays itself out.
        double baseline = FirstBaseline;
        if (Math.Abs(baseline - _drawnBaseline) > 0.01)
        {
            _drawnBaseline = baseline;
            Redraw();
        }
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        double baseline = FirstBaseline;
        if (_task != TaskState.None)
        {
            return;
        }

        _marker ??= CreateLabel(MarkerText(), Sheet.FontSize);
        float width = (float)_marker.LayoutBounds.Width;
        float x = (float)Sheet.ListIndent - width - 6;
        session.DrawTextLayout(_marker, new Vector2(Math.Max(0, x), (float)baseline - _marker.LineMetrics[0].Baseline), Context.Foreground);
    }

    /// <summary>The task checkbox in this control's coordinates, or null for an item without one; a click toggles it (10).</summary>
    internal Rect? CheckboxBounds
    {
        get
        {
            if (_task == TaskState.None)
            {
                return null;
            }

            float size = Sheet.FontSize * 0.85f;
            float x = (float)Sheet.ListIndent - size - 8;
            float top = (float)FirstBaseline - size * 0.9f;
            return new Rect(Math.Max(0, x), top, size, size);
        }
    }

    private void UpdateCheckBox()
    {
        if (_task == TaskState.None)
        {
            DetachCheckBox();
            return;
        }

        if (_checkBox is null)
        {
            _checkBox = new CheckBox
            {
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(0),
                IsTabStop = false,
                AllowFocusOnInteraction = false,
                RenderTransformOrigin = new Point(0.5, 0.5),
            };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_checkBox, Localization.UiStrings.Get("editor.taskCheckbox"));
            _checkBox.Checked += OnCheckBoxToggled;
            _checkBox.Unchecked += OnCheckBoxToggled;
            AddDecoration(_checkBox);
        }

        ShowTask();
    }

    internal void RefreshLanguage()
    {
        if (_checkBox is not null)
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_checkBox, Localization.UiStrings.Get("editor.taskCheckbox"));
        }
    }

    private void DetachCheckBox()
    {
        if (_checkBox is not null)
        {
            _checkBox.Checked -= OnCheckBoxToggled;
            _checkBox.Unchecked -= OnCheckBoxToggled;
            RemoveDecoration(_checkBox);
            _checkBox = null;
        }
    }

    private void ShowTask()
    {
        _syncing = true;
        _checkBox!.IsChecked = _task == TaskState.Checked;
        _syncing = false;
    }

    /// <summary>Toggled by a click or by UI Automation, not by <see cref="ShowTask"/>.</summary>
    private void OnCheckBoxToggled(object sender, RoutedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        // The box shows the document, not its own toggle: the command decides, the projection answers.
        ShowTask();
        for (DependencyObject? parent = this; parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is MarkdownBox box)
            {
                box.ToggleTask(BlockId);
                return;
            }
        }
    }

    private string MarkerText() => _number > 0 ? $"{_number}." : Bullets[(Math.Max(1, _level) - 1) % Bullets.Length];

    private void SetMarker<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            DropMarker();
            Redraw();
        }
    }

    private void DropMarker()
    {
        _marker?.Dispose();
        _marker = null;
    }
}
