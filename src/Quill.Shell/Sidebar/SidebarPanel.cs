using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;

namespace Quill.Shell.Sidebar;

/// <summary>The tabs of the sidebar.</summary>
public enum SidebarTab
{
    Outline,
    Files,
}

/// <summary>The sidebar: Outline and Files tabs (the toolkit's <see cref="Segmented"/>) over the panel of the chosen tab.</summary>
public sealed partial class SidebarPanel : UserControl
{
    private readonly Segmented _tabs = new() { Margin = new Thickness(12, 10, 12, 6), HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly SegmentedItem _outlineTab = new() { Icon = new FontIcon { Glyph = "\uE8FD" } };
    private readonly SegmentedItem _filesTab = new() { Icon = new FontIcon { Glyph = "\uE8B7" } };
    private readonly Border _host = new();

    public SidebarPanel(OutlinePanel outline, FilesPanel files)
    {
        Outline = outline;
        Files = files;
        _tabs.Items.Add(_outlineTab);
        _tabs.Items.Add(_filesTab);
        _tabs.SelectedItem = _outlineTab;
        _host.Child = outline;
        _tabs.SelectionChanged += (_, _) =>
        {
            bool showFiles = ReferenceEquals(_tabs.SelectedItem, _filesTab);
            UIElement target = showFiles ? files : outline;
            if (!ReferenceEquals(_host.Child, target))
            {
                _host.Child = target;
            }

            if (showFiles)
            {
                files.Refresh();
            }

            TabChanged?.Invoke(this, EventArgs.Empty);
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(_tabs);
        Grid.SetRow(_host, 1);
        layout.Children.Add(_host);
        Content = layout;

        ApplyLanguage();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                UiStrings.LanguageChanged -= OnLanguageChanged;
            }
        };
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    private void ApplyLanguage()
    {
        _outlineTab.Content = UiStrings.Get("sidebar.tab.outline");
        _filesTab.Content = UiStrings.Get("sidebar.tab.files");
        Outline.RefreshLanguage();
        Files.RefreshLanguage();
    }

    public event EventHandler? TabChanged;

    public OutlinePanel Outline { get; }

    public FilesPanel Files { get; }

    public SidebarTab Tab
    {
        get => ReferenceEquals(_host.Child, Files) ? SidebarTab.Files : SidebarTab.Outline;
        set => _tabs.SelectedItem = value == SidebarTab.Files ? _filesTab : _outlineTab;
    }

    /// <summary>Whether the outline is the tab shown.</summary>
    public bool ShowsOutline => Tab == SidebarTab.Outline;
}
