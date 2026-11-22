using System.Collections.Immutable;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Input;
using Quill.Editor.WinUI.Localization;

namespace Quill.Editor.WinUI.Input;

/// <summary>
/// The Edit, Paragraph, Format and View menus of a window's menu bar (M6.0, M6.1, after Typora's): built from
/// <see cref="EditorMenus"/> once, then brought up to date in place a moment after the editor changes
/// (labels, enabled and checked states). Items are never cleared and re-added: a menu whose shape
/// changes is replaced as a whole. The shortcuts are shown as text; the editor handles the keys.
/// </summary>
public sealed class EditorMenuBar
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(150);

    private readonly MenuBar _bar;
    private readonly MarkdownBox _editor;
    private readonly List<(string Id, Func<MenuContext, ImmutableArray<MenuEntry>> Build, MenuBarItem? Item)> _menus = [];
    private readonly DispatcherQueueTimer _timer;
    private readonly Dictionary<string, List<MenuFlyoutItemBase>> _extras = new(StringComparer.OrdinalIgnoreCase);

    public EditorMenuBar(MenuBar bar, MarkdownBox editor)
    {
        _bar = bar;
        _editor = editor;
        _menus.Add(("edit", EditorMenus.Edit, null));
        _menus.Add(("paragraph", EditorMenus.Paragraph, null));
        _menus.Add(("format", EditorMenus.Format, null));
        _menus.Add(("view", EditorMenus.View, null));
        _timer = bar.DispatcherQueue.CreateTimer();
        _timer.Interval = RefreshDelay;
        _timer.IsRepeating = false;
        _timer.Tick += (_, _) => Refresh();
        editor.Changed += (_, _) => _timer.Start();
        bar.Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            Refresh();
        };
        bar.Unloaded += (_, _) => UiStrings.LanguageChanged -= OnLanguageChanged;
    }

    /// <summary>The menu bar items, in order, once the first refresh created them.</summary>
    public IReadOnlyList<MenuBarItem> Items => [.. _menus.Select(static menu => menu.Item).OfType<MenuBarItem>()];

    /// <summary>Adds a window's own item at the end of an editor menu (View → Sidebar), kept across rebuilds.</summary>
    public void Append(string menuId, MenuFlyoutItemBase extra)
    {
        string key = menuId.ToLowerInvariant();
        if (!_extras.TryGetValue(key, out var list))
        {
            _extras[key] = list = [];
        }

        list.Add(extra);
        if (_menus.FirstOrDefault(menu => string.Equals(menu.Id, key, StringComparison.OrdinalIgnoreCase)).Item is { } item)
        {
            item.Items.Add(extra);
        }
    }

    /// <summary>Brings the menus up to date with the editor now.</summary>
    public void Refresh()
    {
        if (!_editor.HasDocument)
        {
            return;
        }

        var context = _editor.MenuContext();
        for (int i = 0; i < _menus.Count; i++)
        {
            var (id, build, item) = _menus[i];
            string title = UiStrings.Get($"menu.top.{id}");
            var entries = build(context);
            var extras = _extras.TryGetValue(id, out var list) ? list : [];
            if (item is not null)
            {
                item.Title = title;
                foreach (var extra in extras)
                {
                    item.Items.Remove(extra);
                }

                bool updated = MenuItems.Update(item.Items, entries);
                foreach (var extra in extras)
                {
                    if (updated)
                    {
                        item.Items.Add(extra);
                    }
                }

                if (updated)
                {
                    continue;
                }
            }

            var created = new MenuBarItem { Title = title };
            foreach (var menuItem in MenuItems.Create(entries, entry => _ = _editor.RunMenuItemAsync(entry)))
            {
                created.Items.Add(menuItem);
            }

            foreach (var extra in extras)
            {
                created.Items.Add(extra);
            }

            // After the menus before it (the window's own menus, such as File, come first).
            int at = item is null ? _bar.Items.Count : _bar.Items.IndexOf(item);
            if (item is not null)
            {
                _bar.Items.Remove(item);
            }

            _bar.Items.Insert(at, created);
            _menus[i] = (id, build, created);
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Refresh();
}
