using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;

namespace Quill.Shell.Themes;

/// <summary>
/// The controls of the theme form, one per kind of slot (15-themes.md): colors as a swatch with the hex text
/// that opens a <see cref="ColorPicker"/>, lengths, scales and counts as number boxes, fonts as an editable
/// list of the system's fonts, toggles, lists, and paddings as four number boxes. Each shows the edited sheet
/// (refreshed on reset) and changes it.
/// </summary>
public sealed partial class ThemeEditorPage
{
    private static IReadOnlyList<string>? _fonts;

    /// <summary>The system's font families, sorted.</summary>
    private static IReadOnlyList<string> Fonts => _fonts ??= [.. CanvasTextFormat.GetSystemFontFamilies().Distinct().OrderBy(static name => name, StringComparer.CurrentCultureIgnoreCase)];

    private FrameworkElement ControlFor(StyleSlot slot) => slot.Kind switch
    {
        StyleSlotKind.Color or StyleSlotKind.OptionalColor => ColorControl(slot),
        StyleSlotKind.Length or StyleSlotKind.Scale or StyleSlotKind.Integer => NumberControl(slot),
        StyleSlotKind.Font => FontControl(slot),
        StyleSlotKind.Toggle => ToggleControl(slot),
        StyleSlotKind.Choice => ChoiceControl(slot),
        _ => PaddingControl(slot),
    };

    private FrameworkElement ColorControl(StyleSlot slot)
    {
        var swatch = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = new SolidColorBrush(Colors.Gray) };
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
        var picker = new ColorPicker
        {
            IsAlphaEnabled = true,
            IsMoreButtonVisible = false,
            IsColorChannelTextInputVisible = false,
            IsHexInputVisible = true,
        };
        var none = new CheckBox { Visibility = slot.Kind == StyleSlotKind.OptionalColor ? Visibility.Visible : Visibility.Collapsed };
        var button = new Button
        {
            Width = ControlWidth,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { swatch, text } },
            Flyout = new Flyout { Content = new StackPanel { Spacing = 8, Children = { picker, none } } },
        };

        void Show(string value)
        {
            var color = StyleSlot.ParseColor(value);
            swatch.Background = color is { } c ? new SolidColorBrush(c) : null;
            text.Text = value.Length == 0 ? UiStrings.Get("themeEditor.none") : value;
            none.IsChecked = value.Length == 0;
            if (color is { } shown)
            {
                picker.Color = shown;
            }
        }

        _localizers.Add(() =>
        {
            none.Content = UiStrings.Get("themeEditor.none");
            if (slot.Get(_sheet).Length == 0)
            {
                text.Text = UiStrings.Get("themeEditor.none");
            }
        });
        _refreshers.Add(() => Show(slot.Get(_sheet)));
        picker.ColorChanged += (_, args) =>
        {
            if (!_refreshing)
            {
                Change(slot, StyleSlot.FormatColor(args.NewColor));
                Show(slot.Get(_sheet));
            }
        };
        none.Click += (_, _) =>
        {
            Change(slot, none.IsChecked == true ? string.Empty : StyleSlot.FormatColor(picker.Color));
            Show(slot.Get(_sheet));
        };
        return button;
    }

    private FrameworkElement NumberControl(StyleSlot slot)
    {
        bool scale = slot.Kind == StyleSlotKind.Scale;
        var box = new NumberBox
        {
            Width = ControlWidth,
            Minimum = slot.Minimum,
            Maximum = slot.Maximum,
            SmallChange = scale ? 0.05 : 1,
            LargeChange = scale ? 0.25 : 10,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        };
        if (slot.Kind == StyleSlotKind.Length)
        {
            box.Header = null;
            box.PlaceholderText = "px";
        }

        _refreshers.Add(() => box.Value = StyleSlot.ParseNumber(slot.Get(_sheet)) ?? double.NaN);
        box.ValueChanged += (_, args) =>
        {
            if (!double.IsNaN(args.NewValue))
            {
                Change(slot, slot.Kind == StyleSlotKind.Integer ? Math.Round(args.NewValue).ToString(System.Globalization.CultureInfo.InvariantCulture) : StyleSlot.FormatNumber(args.NewValue));
            }
        };
        return box;
    }

    private FrameworkElement FontControl(StyleSlot slot)
    {
        var box = new ComboBox { Width = ControlWidth, IsEditable = true, ItemsSource = Fonts };
        _refreshers.Add(() =>
        {
            string font = slot.Get(_sheet);
            box.SelectedItem = Fonts.FirstOrDefault(name => string.Equals(name, font, StringComparison.OrdinalIgnoreCase));
            box.Text = font;
        });
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedItem is string font)
            {
                Change(slot, font);
            }
        };
        box.TextSubmitted += (_, args) =>
        {
            Change(slot, args.Text);
            args.Handled = true;
        };
        return box;
    }

    private FrameworkElement ToggleControl(StyleSlot slot)
    {
        var toggle = new ToggleSwitch { MinWidth = 0 };
        _localizers.Add(() =>
        {
            toggle.OnContent = UiStrings.Get("settings.on");
            toggle.OffContent = UiStrings.Get("settings.off");
        });
        _refreshers.Add(() => toggle.IsOn = slot.Get(_sheet) == "true");
        toggle.Toggled += (_, _) => Change(slot, toggle.IsOn ? "true" : "false");
        return toggle;
    }

    private FrameworkElement ChoiceControl(StyleSlot slot)
    {
        var box = new ComboBox { Width = ControlWidth };
        _localizers.Add(() =>
        {
            int selected = box.SelectedIndex;
            box.Items.Clear();
            foreach (var (value, label) in slot.Options)
            {
                box.Items.Add(StyleSlots.OptionLabel(value, label));
            }

            box.SelectedIndex = selected;
        });

        _refreshers.Add(() => box.SelectedIndex = slot.Options.ToList().FindIndex(option => option.Value == slot.Get(_sheet)));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < slot.Options.Count)
            {
                Change(slot, slot.Options[box.SelectedIndex].Value);
            }
        };
        return box;
    }

    /// <summary>Left, top, right, bottom.</summary>
    private FrameworkElement PaddingControl(StyleSlot slot)
    {
        var boxes = Enumerable.Range(0, 4).Select(_ => new NumberBox
        {
            Width = (ControlWidth - 3 * 4) / 4,
            Minimum = slot.Minimum,
            Maximum = slot.Maximum,
            SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
            ValidationMode = NumberBoxValidationMode.InvalidInputOverwritten,
        }).ToList();
        string[] sideKeys = ["themeEditor.side.left", "themeEditor.side.top", "themeEditor.side.right", "themeEditor.side.bottom"];
        _localizers.Add(() =>
        {
            for (int i = 0; i < 4; i++)
            {
                string side = UiStrings.Get(sideKeys[i]);
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(boxes[i], $"{slot.DisplayName}, {side.ToLower(UiStrings.Culture)}");
                ToolTipService.SetToolTip(boxes[i], side);
            }
        });

        _refreshers.Add(() =>
        {
            var parts = slot.Get(_sheet).Split(',');
            for (int i = 0; i < 4; i++)
            {
                boxes[i].Value = StyleSlot.ParseNumber(parts.ElementAtOrDefault(i) ?? "0") ?? 0;
            }
        });
        foreach (var box in boxes)
        {
            box.ValueChanged += (_, _) =>
            {
                if (boxes.All(static b => !double.IsNaN(b.Value)))
                {
                    Change(slot, string.Join(",", boxes.Select(static b => StyleSlot.FormatNumber(b.Value))));
                }
            };
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var box in boxes)
        {
            panel.Children.Add(box);
        }

        return panel;
    }
}
