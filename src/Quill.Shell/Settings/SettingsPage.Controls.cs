using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;

namespace Quill.Shell.Settings;

/// <summary>
/// The cards and controls of the settings page. Every control shows a value of <see cref="AppSettings"/>
/// (refreshed from the store) and writes it back when the user changes it; controls of a kind share a width.
/// </summary>
public sealed partial class SettingsPage
{
    /// <summary>The width of choice boxes and text boxes on the cards.</summary>
    internal const double ControlWidth = 220;

    /// <summary>Adds a card: icon, name and description at the left, the control at the right.</summary>
    private SettingsCard Add(
        SettingsSection section,
        string glyph,
        string nameKey,
        string descriptionKey,
        object? content,
        bool available = true,
        Func<string>? descriptionOverride = null)
    {
        var card = new SettingsCard
        {
            HeaderIcon = new FontIcon { Glyph = glyph },
            Content = content,
            IsEnabled = available,
        };
        var entry = new SettingEntry(section, string.Empty, string.Empty, card, available);
        _entries.Add(entry);
        _localizers.Add(() =>
        {
            string name = UiStrings.Get(nameKey);
            string baseDescription = UiStrings.Get(descriptionKey);
            string shownDescription = descriptionOverride is null
                ? (available ? baseDescription : baseDescription + UiStrings.Get("settings.notAvailable"))
                : descriptionOverride();
            entry.Name = name;
            entry.Description = shownDescription;
            card.Header = name;
            card.Description = shownDescription;
            AutomationProperties.SetName(card, name);
        });
        return card;
    }

    private ToggleSwitch Toggle(string nameKey, Func<AppSettings, bool> get, Func<AppSettings, bool, AppSettings> set)
    {
        var toggle = new ToggleSwitch { MinWidth = 0 };
        _localizers.Add(() =>
        {
            toggle.OnContent = UiStrings.Get("settings.on");
            toggle.OffContent = UiStrings.Get("settings.off");
            AutomationProperties.SetName(toggle, UiStrings.Get(nameKey));
        });
        toggle.Toggled += (_, _) => Change(settings => set(settings, toggle.IsOn));
        _refreshers.Add(settings => toggle.IsOn = get(settings));
        return toggle;
    }

    private ComboBox Choice<T>(string nameKey, IReadOnlyList<(T Value, string TextKey)> options, Func<AppSettings, T> get, Func<AppSettings, T, AppSettings> set)
    {
        var box = new ComboBox { Width = ControlWidth };
        LocalizeComboBox(box, nameKey, () => options.Select(static option => UiStrings.Get(option.TextKey)));
        box.SelectionChanged += (_, _) =>
        {
            if (box.SelectedIndex >= 0 && box.SelectedIndex < options.Count)
            {
                Change(settings => set(settings, options[box.SelectedIndex].Value));
            }
        };
        _refreshers.Add(settings =>
        {
            var value = get(settings);
            box.SelectedIndex = Math.Max(0, options.ToList().FindIndex(option => EqualityComparer<T>.Default.Equals(option.Value, value)));
        });
        return box;
    }

    private void LocalizeComboBox(ComboBox box, string nameKey, Func<IEnumerable<string>> texts) =>
        _localizers.Add(() =>
        {
            int selected = box.SelectedIndex;
            AutomationProperties.SetName(box, UiStrings.Get(nameKey));
            box.Items.Clear();
            foreach (string text in texts())
            {
                box.Items.Add(text);
            }

            box.SelectedIndex = selected;
        });

    private TextBox TextField(string nameKey, string placeholder)
    {
        var box = new TextBox { Width = ControlWidth, PlaceholderText = placeholder };
        _localizers.Add(() => AutomationProperties.SetName(box, UiStrings.Get(nameKey)));
        return box;
    }

    private Button BrowseButton(string nameKey)
    {
        var button = new Button();
        _localizers.Add(() =>
        {
            button.Content = UiStrings.Get("settings.browse");
            AutomationProperties.SetName(button, UiStrings.Get(nameKey));
        });
        return button;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var child in children)
        {
            row.Children.Add(child);
        }

        return row;
    }

    private static T Named<T>(T element, string name)
        where T : DependencyObject
    {
        AutomationProperties.SetName(element, name);
        return element;
    }

    /// <summary>The last part of a folder's path ("notes" for C:\docs\notes); a drive's root as it is.</summary>
    internal static string FolderName(string folder)
    {
        string trimmed = folder.TrimEnd('\\', '/');
        string name = Path.GetFileName(trimmed);
        return name.Length > 0 ? name : folder;
    }
}
