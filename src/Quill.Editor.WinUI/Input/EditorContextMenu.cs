using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Input;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The editor's context menu (Q73, M6.0; layout after Typora's, look of the Windows 11 text menu): the
/// icon row holds cut, copy, paste and delete, then bold, italic, code and link; below it the "Copy /
/// paste as", "Paragraph" and "Insert" submenus, undo, redo and select all. The entries come from
/// <see cref="EditorMenus.Context"/>; the menu only reports which one was chosen.
/// </summary>
internal static class EditorContextMenu
{
    public static CommandBarFlyout Create(IEnumerable<MenuEntry> primary, IEnumerable<MenuEntry> secondary, Action<MenuEntry> invoke)
    {
        var menu = new CommandBarFlyout { AlwaysExpanded = true };
        void Run(MenuEntry entry)
        {
            menu.Hide();
            invoke(entry);
        }

        foreach (var entry in primary)
        {
            string label = UiStrings.MenuLabel(entry);
            var button = new AppBarButton { Label = label, Icon = MenuItems.Icon(entry.Id), IsEnabled = entry.IsEnabled };
            ToolTipService.SetToolTip(button, entry.Shortcut is null ? label : $"{label} ({entry.Shortcut})");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
            button.Click += (_, _) => Run(entry);
            menu.PrimaryCommands.Add(button);
        }

        foreach (var entry in secondary)
        {
            menu.SecondaryCommands.Add(Secondary(entry, Run));
        }

        return menu;
    }

    private static ICommandBarElement Secondary(MenuEntry entry, Action<MenuEntry> run)
    {
        if (entry.IsSeparator)
        {
            return new AppBarSeparator();
        }

        string label = UiStrings.MenuLabel(entry);
        var button = new AppBarButton { Label = label, Icon = MenuItems.Icon(entry.Id), IsEnabled = entry.IsEnabled };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        if (!entry.Children.IsEmpty)
        {
            // A submenu: the button opens a menu flyout to its side.
            var flyout = new MenuFlyout { Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.RightEdgeAlignedTop };
            foreach (var item in MenuItems.Create(entry.Children, run))
            {
                flyout.Items.Add(item);
            }

            button.Flyout = flyout;
            return button;
        }

        if (entry.Shortcut is { } shortcut)
        {
            button.KeyboardAcceleratorTextOverride = shortcut;
        }

        button.Click += (_, _) => run(entry);
        return button;
    }
}
