using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;

namespace Quill.Shell.Settings;

/// <summary>
/// The settings page (14-settings.md, after the Windows Settings app): a search box over a fixed list of
/// sections at the left, and the settings at the right, each a <c>SettingsCard</c> under its section's
/// title. Clicking a section scrolls to it; scrolling highlights the section in view. Search shows only the
/// settings whose name or description matches, with their section titles, and turns the list off. A change
/// is saved at once to the store every window shares, and a change made in another window shows here.
/// </summary>
public sealed partial class SettingsPage : UserControl
{
    /// <summary>Below this width the section list hides and the search box goes above the settings.</summary>
    private const double NarrowWidth = 720;

    /// <summary>How far below the top of the view a section's title may be and still count as the one in view.</summary>
    private const double SectionThreshold = 48;

    /// <summary>The room left above a section's title when the list scrolls to it.</summary>
    private const double SectionGap = 12;

    private readonly SettingsStore _store;
    private readonly List<SettingEntry> _entries = [];
    private readonly List<Action<AppSettings>> _refreshers = [];
    private readonly List<Action> _localizers = [];
    private readonly Dictionary<SettingsSection, TextBlock> _headers = [];
    private readonly Dictionary<SettingsSection, TextBlock> _sectionLabels = [];
    private readonly Dictionary<SettingsSection, ListViewItem> _sectionItems = [];
    private readonly Grid _layout = new();
    private readonly AutoSuggestBox _search = new()
    {
        QueryIcon = new SymbolIcon(Symbol.Find),
        Margin = new Thickness(0, 0, 0, 12),
    };
    private readonly ListView _sections = new() { SelectionMode = ListViewSelectionMode.Single };
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    private readonly StackPanel _content = new() { MaxWidth = 1000, Padding = new Thickness(32, 20, 40, 40) };
    private readonly TextBlock _noMatch = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 16, 0, 0) };
    private readonly TextBlock _title = new() { Margin = new Thickness(0, 0, 0, 16) };
    private bool _refreshing;
    private bool _syncing;
    private bool _navigating;
    private bool _narrow;

    public SettingsPage(SettingsStore store, IReadOnlyList<ThemeChoice> themes)
    {
        _store = store;
        Themes = themes;
        _title.Style = (Style)Application.Current.Resources["TitleTextBlockStyle"];
        SyncLanguage(store.Current.Language);
        BuildEntries();
        BuildContent();
        BuildLayout();

        _search.TextChanged += (_, _) => Filter();
        _sections.SelectionChanged += OnSectionSelected;
        _scroller.ViewChanged += OnViewChanged;
        SizeChanged += (_, args) => UpdateLayoutMode(args.NewSize.Width);

        // The store outlives the page's window: listen only while shown, and catch up when shown again.
        Loaded += (_, _) =>
        {
            _store.Changed -= OnStoreChanged;
            _store.Changed += OnStoreChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            SyncLanguage(_store.Current.Language);
            ApplyLanguage();
            Refresh();
        };
        Unloaded += (_, _) =>
        {
            _store.Changed -= OnStoreChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
        };
        ApplyLanguage();
        Refresh();
        Select(SettingsSection.Files);
    }

    /// <summary>"Theme configuration" was clicked: the host shows the theme list (15-themes.md).</summary>
    public event EventHandler? ThemesRequested;

    /// <summary>Asks the user for a folder (the host's picker); null when cancelled or when there is no picker.</summary>
    public Func<Task<string?>>? FolderPicker { get; set; }

    /// <summary>The editor themes to choose from (<see cref="SetThemes"/>).</summary>
    public IReadOnlyList<ThemeChoice> Themes { get; private set; }

    /// <summary>The settings shown, for tests.</summary>
    internal IReadOnlyList<SettingEntry> Entries => _entries;

    internal ScrollViewer Scroller => _scroller;

    internal ListView SectionList => _sections;

    internal TextBlock NoMatchText => _noMatch;

    /// <summary>The section highlighted in the list; null while searching.</summary>
    internal SettingsSection? HighlightedSection => _sections.SelectedItem is ListViewItem { Tag: SettingsSection section } ? section : null;

    /// <summary>The search text; setting it filters as typing would.</summary>
    internal string Query
    {
        get => _search.Text;
        set
        {
            _search.Text = value;
            Filter();
        }
    }

    /// <summary>The title of a section, for tests.</summary>
    internal TextBlock HeaderOf(SettingsSection section) => _headers[section];

    /// <summary>Scrolls to a section, as clicking it in the list does.</summary>
    internal void NavigateTo(SettingsSection section)
    {
        Select(section);
        double target = Math.Clamp(TopOf(_headers[section]) - SectionGap, 0, _scroller.ScrollableHeight);
        if (Math.Abs(target - _scroller.VerticalOffset) < 0.5)
        {
            return;
        }

        // The view settles on the section; until then scrolling does not move the highlight.
        _navigating = true;
        _scroller.ChangeView(null, target, null, disableAnimation: true);
    }

    private static string Title(SettingsSection section) => UiStrings.Get($"settings.section.{section}");

    private static string Glyph(SettingsSection section) => section switch
    {
        SettingsSection.Files => "\uE8B7",
        SettingsSection.Editing => "\uE70F",
        SettingsSection.Images => "\uE91B",
        SettingsSection.Markdown => "\uE943",
        SettingsSection.Appearance => "\uE790",
        _ => "\uE713",
    };

    private void BuildContent()
    {
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            var header = new TextBlock
            {
                Text = Title(section),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(1, section == SettingsSection.Files ? 8 : 30, 0, 6),
            };
            AutomationProperties.SetHeadingLevel(header, AutomationHeadingLevel.Level2);
            _headers[section] = header;
            _content.Children.Add(header);
            foreach (var entry in _entries.Where(entry => entry.Section == section))
            {
                entry.Card.Margin = new Thickness(0, 0, 0, 4);
                _content.Children.Add(entry.Card);
            }

            var labelText = new TextBlock { Text = Title(section), VerticalAlignment = VerticalAlignment.Center };
            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            label.Children.Add(new FontIcon { Glyph = Glyph(section), FontSize = 16 });
            label.Children.Add(labelText);
            _sectionLabels[section] = labelText;

            var item = new ListViewItem
            {
                Tag = section,
                Content = label,
            };
            AutomationProperties.SetName(item, Title(section));
            _sectionItems[section] = item;
            _sections.Items.Add(item);
        }

        _content.Children.Add(_noMatch);
        _scroller.Content = _content;
    }

    private void BuildLayout()
    {
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        _layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _title.Margin = new Thickness(28, 28, 16, 16);
        _search.Margin = new Thickness(24, 0, 16, 12);
        _sections.Margin = new Thickness(20, 0, 12, 12);
        _layout.Children.Add(_title);
        _layout.Children.Add(_search);
        _layout.Children.Add(_sections);
        _layout.Children.Add(_scroller);
        Place(narrow: false);
        Content = _layout;
    }

    /// <summary>Wide: title, search and sections at the left, settings at the right. Narrow: title and search above the settings.</summary>
    private void Place(bool narrow)
    {
        Grid.SetRow(_title, 0);
        Grid.SetRow(_search, 1);
        Grid.SetRow(_sections, 2);
        Grid.SetColumnSpan(_title, narrow ? 2 : 1);
        Grid.SetColumnSpan(_search, narrow ? 2 : 1);
        _search.MaxWidth = narrow ? 480 : double.PositiveInfinity;
        _search.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        _sections.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        Grid.SetRow(_scroller, narrow ? 2 : 0);
        Grid.SetRowSpan(_scroller, narrow ? 1 : 3);
        Grid.SetColumn(_scroller, narrow ? 0 : 1);
        Grid.SetColumnSpan(_scroller, narrow ? 2 : 1);
        _layout.ColumnDefinitions[0].Width = new GridLength(narrow ? 0 : 260);
        _content.Padding = new Thickness(narrow ? 24 : 32, narrow ? 0 : 20, narrow ? 24 : 40, 40);
    }

    private void UpdateLayoutMode(double width)
    {
        if (width <= 0 || !double.IsFinite(width))
        {
            return;
        }

        bool narrow = width < NarrowWidth;
        if (narrow != _narrow)
        {
            _narrow = narrow;
            Place(narrow);
        }
    }

    private void Filter()
    {
        string query = _search.Text.Trim();
        bool searching = query.Length > 0;
        bool any = false;
        foreach (var section in Enum.GetValues<SettingsSection>())
        {
            bool shown = false;
            foreach (var entry in _entries.Where(entry => entry.Section == section))
            {
                bool match = entry.Matches(query);
                entry.Card.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                shown |= match;
            }

            _headers[section].Visibility = shown ? Visibility.Visible : Visibility.Collapsed;
            any |= shown;
        }

        _noMatch.Visibility = any ? Visibility.Collapsed : Visibility.Visible;

        // While searching the list and the highlight that follows scrolling are off.
        _sections.IsEnabled = !searching;
        if (searching)
        {
            _syncing = true;
            _sections.SelectedItem = null;
            _syncing = false;
        }
        else
        {
            _content.UpdateLayout();
            UpdateHighlight();
        }
    }

    private void OnSectionSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!_syncing && HighlightedSection is { } section)
        {
            NavigateTo(section);
        }
    }

    private void OnViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_navigating)
        {
            _navigating = e.IsIntermediate;
            return;
        }

        UpdateHighlight();
    }

    /// <summary>Highlights the last section whose title is at (or above) the top of the view; at the end of the page, the last one.</summary>
    private void UpdateHighlight()
    {
        if (_search.Text.Trim().Length > 0)
        {
            return;
        }

        var sections = Enum.GetValues<SettingsSection>();
        var current = sections[0];
        double offset = _scroller.VerticalOffset;
        if (_scroller.ScrollableHeight > 0 && offset >= _scroller.ScrollableHeight - 1)
        {
            current = sections[^1];
        }
        else
        {
            foreach (var section in sections)
            {
                if (TopOf(_headers[section]) <= offset + SectionThreshold)
                {
                    current = section;
                }
            }
        }

        Select(current);
    }

    private void Select(SettingsSection section)
    {
        var item = _sections.Items.OfType<ListViewItem>().First(item => item.Tag is SettingsSection tag && tag == section);
        if (!ReferenceEquals(_sections.SelectedItem, item))
        {
            _syncing = true;
            _sections.SelectedItem = item;
            _syncing = false;
        }
    }

    /// <summary>Where an element of the content is, from the top of the scrolled content.</summary>
    private double TopOf(FrameworkElement element) => element.TransformToVisual(_content).TransformPoint(default).Y;

    private void OnStoreChanged(object? sender, StoreChangedEventArgs<AppSettings> e)
    {
        SyncLanguage(e.Current.Language);
        Refresh();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLanguage();
        Refresh();
        Filter();
    }

    private static void SyncLanguage(DisplayLanguage language) =>
        UiStrings.SetLanguage(language == DisplayLanguage.Chinese ? UiLanguage.Chinese : UiLanguage.English);

    private void ApplyLanguage()
    {
        _refreshing = true;
        try
        {
            _title.Text = UiStrings.Get("settings.title");
            _search.PlaceholderText = UiStrings.Get("settings.search");
            AutomationProperties.SetName(_search, UiStrings.Get("settings.search"));
            AutomationProperties.SetName(_sections, UiStrings.Get("settings.sections"));
            _noMatch.Text = UiStrings.Get("settings.noMatch");

            foreach (var section in Enum.GetValues<SettingsSection>())
            {
                string title = Title(section);
                _headers[section].Text = title;
                _sectionLabels[section].Text = title;
                AutomationProperties.SetName(_sectionItems[section], title);
            }

            foreach (var localize in _localizers)
            {
                localize();
            }

            UpdateNewMenu();
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Shows the stored settings in the controls; the controls' own events do not write back meanwhile.</summary>
    private void Refresh()
    {
        _refreshing = true;
        try
        {
            foreach (var refresh in _refreshers)
            {
                refresh(_store.Current);
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    /// <summary>Changes the settings, unless the change comes from showing them.</summary>
    private void Change(Func<AppSettings, AppSettings> change)
    {
        if (!_refreshing)
        {
            _store.Update(change);
        }
    }
}
