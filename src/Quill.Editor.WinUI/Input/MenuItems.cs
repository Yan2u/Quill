using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Input;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// Turns <see cref="MenuEntry"/> lists into Windows 11 menu items (M6.0): items with their shortcut
/// text, check marks for checked entries, submenus and separators. The same entries feed the context
/// menu and the window's menu bar. Icons come from Segoe Fluent Icons by entry ID.
/// </summary>
public static class MenuItems
{
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons,Segoe MDL2 Assets");

    /// <summary>Creates the items of a menu; <paramref name="invoke"/> runs a chosen entry.</summary>
    public static IEnumerable<MenuFlyoutItemBase> Create(IEnumerable<MenuEntry> entries, Action<MenuEntry> invoke)
    {
        foreach (var entry in entries)
        {
            yield return Create(entry, invoke);
        }
    }

    public static MenuFlyoutItemBase Create(MenuEntry entry, Action<MenuEntry> invoke)
    {
        if (entry.IsSeparator)
        {
            return new MenuFlyoutSeparator();
        }

        string label = UiStrings.MenuLabel(entry);
        if (!entry.Children.IsEmpty)
        {
            var submenu = new MenuFlyoutSubItem { Text = label, IsEnabled = entry.IsEnabled, Icon = Icon(entry.Id) };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(submenu, label);
            foreach (var child in Create(entry.Children, invoke))
            {
                submenu.Items.Add(child);
            }

            return submenu;
        }

        MenuFlyoutItem item = entry.IsCheckable || entry.IsChecked
            ? new ToggleMenuFlyoutItem { IsChecked = entry.IsChecked }
            : new MenuFlyoutItem();
        item.Text = label;
        item.IsEnabled = entry.IsEnabled;
        item.Icon = item is ToggleMenuFlyoutItem ? null : Icon(entry.Id);
        if (entry.Shortcut is { } shortcut)
        {
            item.KeyboardAcceleratorTextOverride = shortcut;
        }

        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label);
        item.Click += (_, _) => invoke(entry);
        return item;
    }

    /// <summary>
    /// Brings items made by <see cref="Create(IEnumerable{MenuEntry}, Action{MenuEntry})"/> up to date with
    /// new entries of the same menu: labels, enabled and checked states change in place. Returns false
    /// when the menu's shape differs, so the caller creates the items anew.
    /// </summary>
    public static bool Update(IList<MenuFlyoutItemBase> items, IReadOnlyList<MenuEntry> entries)
    {
        if (items.Count != entries.Count)
        {
            return false;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            string label = entry.IsSeparator ? string.Empty : UiStrings.MenuLabel(entry);
            switch (items[i])
            {
                case MenuFlyoutSeparator when entry.IsSeparator:
                    break;
                case MenuFlyoutSubItem submenu when !entry.Children.IsEmpty:
                    submenu.Text = label;
                    submenu.IsEnabled = entry.IsEnabled;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(submenu, label);
                    if (!Update(submenu.Items, entry.Children))
                    {
                        return false;
                    }

                    break;
                case ToggleMenuFlyoutItem toggle when entry.Children.IsEmpty && !entry.IsSeparator && (entry.IsCheckable || entry.IsChecked):
                    toggle.Text = label;
                    toggle.IsEnabled = entry.IsEnabled;
                    toggle.IsChecked = entry.IsChecked;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, label);
                    break;
                case MenuFlyoutItem item and not ToggleMenuFlyoutItem when entry.Children.IsEmpty && !entry.IsSeparator && !entry.IsCheckable && !entry.IsChecked:
                    item.Text = label;
                    item.IsEnabled = entry.IsEnabled;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, label);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>The icon of an entry, or null for entries shown as text only.</summary>
    public static IconElement? Icon(string id) => Glyph(id) is { } glyph ? new FontIcon { Glyph = glyph, FontFamily = IconFont } : null;

    public static string? Glyph(string id) => id switch
    {
        "cut" => "\uE8C6",
        "copy" => "\uE8C8",
        "paste" => "\uE77F",
        "delete" => "\uE74D",
        "undo" => "\uE7A7",
        "redo" => "\uE7A6",
        "select-all" => "\uE8B3",
        "bold" => "\uE8DD",
        "italic" => "\uE8DB",
        "underline" => "\uE8DC",
        "code" => "\uE943",
        "strikethrough" => "\uEDE0",
        "link" => "\uE71B",
        "image" or "insert-image" => "\uEB9F",
        "quote" => "\uE9B2",
        "ordered-list" => "\uE9D5",
        "bullet-list" => "\uE8FD",
        "task-list" => "\uE73A",
        "table" or "insert-table" => "\uE80A",
        "copy-paste-as" => "\uE8C8",
        "insert" => "\uE710",
        "clear-format" => "\uE894",
        "reveal" => "\uE8A7",
        _ => null,
    };
}
