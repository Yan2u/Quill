using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;

namespace Quill.Shell.Themes;

/// <summary>
/// The configuration page of a theme (15-themes.md): at the left a form, the name and description, then every
/// style slot by group (made from <see cref="StyleSlots"/>), filtered by the search box above it; at the right
/// the sample document rendered with the edited theme, scrollable and read-only. Changes show in the preview
/// at once; Save writes the theme, Reset goes back to what was saved. A built-in theme is read-only: the form
/// is disabled and Copy makes an editable copy.
/// </summary>
public sealed partial class ThemeEditorPage : UserControl
{
    private readonly ThemeCatalog _catalog;
    private readonly ThemePreview _preview = new(ThemePreview.FullSample) { CanScroll = true };
    private readonly StackPanel _form = new() { Spacing = 4, Padding = new Thickness(20, 0, 20, 24) };
    private readonly AutoSuggestBox _search = new() { QueryIcon = new SymbolIcon(Symbol.Find), Margin = new Thickness(20, 0, 20, 12) };
    private readonly TextBlock _title = new() { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBox _name = new() { Width = ControlWidth };
    private readonly TextBox _description = new() { Width = ControlWidth, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false };
    private readonly Button _back = new() { Content = new FontIcon { Glyph = "\uE72B", FontSize = 14 }, Padding = new Thickness(10, 6, 10, 6) };
    private readonly Button _save = new();
    private readonly Button _reset = new();
    private readonly Button _copy = new();
    private readonly List<(StyleSlot Slot, FrameworkElement Row)> _rows = [];
    private readonly Dictionary<ThemeGroup, TextBlock> _headers = [];
    private readonly List<Action> _refreshers = [];
    private readonly List<Action> _localizers = [];
    private ThemeInfo _theme;
    private MdStyleSheet _sheet;
    private bool _refreshing;
    private UiLanguage? _appliedLanguage;

    public ThemeEditorPage(ThemeCatalog catalog, ThemeInfo theme)
    {
        _catalog = catalog;
        _theme = theme;
        _sheet = theme.Sheet;
        _title.Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"];
        _save.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        BuildForm();
        Content = BuildLayout();

        _search.TextChanged += (_, _) => Filter();
        _name.TextChanged += (_, _) => Edited();
        _description.TextChanged += (_, _) => Edited();
        _save.Click += (_, _) => Save();
        _reset.Click += (_, _) => Reset();
        _copy.Click += (_, _) => Copied?.Invoke(this, _catalog.Copy(_theme.Id));
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            if (_appliedLanguage != UiStrings.Language)
            {
                ApplyLanguage();
            }
        };
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                UiStrings.LanguageChanged -= OnLanguageChanged;
                ReleaseColorFlyout();
            }
        };
        ApplyLanguage();
        Reset();
    }

    /// <summary>The back button: the host shows the theme list again.</summary>
    public event EventHandler? BackRequested;

    /// <summary>Copy made a new theme: the host may open it.</summary>
    public event EventHandler<ThemeInfo>? Copied;

    /// <summary>The width of the form's controls: all of a kind are as wide.</summary>
    internal const double ControlWidth = 220;

    public ThemeInfo Theme => _theme;

    /// <summary>The sheet as edited, for tests.</summary>
    internal MdStyleSheet Sheet => _sheet;

    internal ThemePreview Preview => _preview;

    internal bool HasChanges { get; private set; }

    internal Button SaveButton => _save;

    internal Button ResetButton => _reset;

    internal Button CopyButton => _copy;

    internal TextBox NameBox => _name;

    /// <summary>The rows of the form, for tests.</summary>
    internal IReadOnlyList<(StyleSlot Slot, FrameworkElement Row)> Rows => _rows;

    internal TextBlock HeaderOf(ThemeGroup group) => _headers[group];

    internal string Query
    {
        get => _search.Text;
        set
        {
            _search.Text = value;
            Filter();
        }
    }

    /// <summary>Changes a slot as its control does.</summary>
    internal void Change(StyleSlot slot, string value)
    {
        if (_refreshing || _theme.IsBuiltIn || slot.Set(_sheet, value) is not { } changed || changed == _sheet)
        {
            return;
        }

        _sheet = changed;
        _preview.Sheet = _sheet;
        Edited();
    }

    /// <summary>Writes the theme; the catalog tells every window.</summary>
    internal void Save()
    {
        if (_theme.IsBuiltIn)
        {
            return;
        }

        _theme = _catalog.Save(_theme.Id, _name.Text, _description.Text, _sheet);
        Reset();
    }

    /// <summary>Goes back to the theme as last saved.</summary>
    internal void Reset()
    {
        _theme = _catalog.Find(_theme.Id) ?? _theme;
        _sheet = _theme.Sheet;
        _preview.Sheet = _sheet;
        _refreshing = true;
        try
        {
            _title.Text = TitleText();
            _name.Text = _theme.Name;
            _description.Text = _theme.IsBuiltIn
                ? BuiltInThemes.Find(_theme.Id)?.LocalizedDescription ?? _theme.Description
                : _theme.Description;
            foreach (var refresh in _refreshers)
            {
                refresh();
            }
        }
        finally
        {
            _refreshing = false;
        }

        HasChanges = false;
        UpdateButtons();
        if (_colorFlyout is { IsOpen: true })
        {
            _colorFlyout.Hide();
        }
    }

    private string TitleText() => _theme.IsBuiltIn ? UiStrings.Format("themeEditor.builtInSuffix", _theme.Name) : _theme.Name;

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLanguage();
        Filter();
    }

    private void ApplyLanguage()
    {
        _appliedLanguage = UiStrings.Language;
        _refreshing = true;
        try
        {
            _search.PlaceholderText = UiStrings.Get("themeEditor.search");
            AutomationProperties.SetName(_search, UiStrings.Get("themeEditor.searchName"));
            AutomationProperties.SetName(_name, UiStrings.Get("themeEditor.nameField"));
            AutomationProperties.SetName(_description, UiStrings.Get("themeEditor.descriptionField"));
            _save.Content = UiStrings.Get("themeEditor.save");
            _reset.Content = UiStrings.Get("themeEditor.reset");
            _copy.Content = UiStrings.Get("themeEditor.copy");
            string backTip = UiStrings.Get("themeEditor.back");
            AutomationProperties.SetName(_back, backTip);
            ToolTipService.SetToolTip(_back, backTip);
            _title.Text = TitleText();
            if (_theme.IsBuiltIn)
            {
                _description.Text = BuiltInThemes.Find(_theme.Id)?.LocalizedDescription ?? _theme.Description;
            }

            foreach (var group in Enum.GetValues<ThemeGroup>())
            {
                _headers[group].Text = StyleSlots.Title(group);
            }

            if (_colorNone is not null)
            {
                _colorNone.Content = UiStrings.Get("themeEditor.none");
            }

            foreach (var localize in _localizers)
            {
                localize();
            }
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Edited()
    {
        if (_refreshing)
        {
            return;
        }

        HasChanges = true;
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        _save.Visibility = _theme.IsBuiltIn ? Visibility.Collapsed : Visibility.Visible;
        _reset.Visibility = _save.Visibility;
        _save.IsEnabled = HasChanges;
        _reset.IsEnabled = HasChanges;
    }

    private Grid BuildLayout()
    {
        _back.Click += (_, _) => BackRequested?.Invoke(this, EventArgs.Empty);
        var header = new Grid { ColumnSpacing = 12, Padding = new Thickness(20, 16, 20, 16) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _copy, _reset, _save } };
        header.Children.Add(_back);
        Grid.SetColumn(_title, 1);
        header.Children.Add(_title);
        Grid.SetColumn(buttons, 2);
        header.Children.Add(buttons);

        // The form is disabled for a built-in theme; the search stays usable.
        var form = new ContentControl
        {
            Content = _form,
            IsEnabled = !_theme.IsBuiltIn,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsTabStop = false,
        };
        var left = new Grid();
        left.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        left.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        left.Children.Add(_search);
        var scroller = new ScrollViewer { Content = form, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Grid.SetRow(scroller, 1);
        left.Children.Add(scroller);

        var preview = Themed.Load<Border>("Border", "BorderThickness='1,0,0,0' BorderBrush='{ThemeResource DividerStrokeColorDefaultBrush}'");
        preview.Child = _preview;

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(480) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(left);
        Grid.SetColumn(preview, 1);
        body.Children.Add(preview);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.Children.Add(header);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);
        return layout;
    }

    private void BuildForm()
    {
        _form.Children.Add(Row(() => UiStrings.Get("themeEditor.nameLabel"), _name));
        _form.Children.Add(Row(() => UiStrings.Get("themeEditor.descriptionLabel"), _description));
        foreach (var group in Enum.GetValues<ThemeGroup>())
        {
            var header = new TextBlock
            {
                Text = StyleSlots.Title(group),
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                Margin = new Thickness(0, 24, 0, 4),
            };
            AutomationProperties.SetHeadingLevel(header, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level2);
            _headers[group] = header;
            _form.Children.Add(header);
            foreach (var slot in StyleSlots.All.Where(slot => slot.Group == group))
            {
                var row = Row(() => slot.DisplayName, ControlFor(slot));
                _rows.Add((slot, row));
                _form.Children.Add(row);
            }
        }
    }

    /// <summary>A row of the form: the label at the left, the control at the right.</summary>
    private Grid Row(Func<string> getLabel, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 12, MinHeight = 40 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        control.VerticalAlignment = VerticalAlignment.Center;
        _localizers.Add(() =>
        {
            string label = getLabel();
            text.Text = label;
            AutomationProperties.SetName(control, label);
        });
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    private void Filter()
    {
        string query = _search.Text.Trim();
        foreach (var group in Enum.GetValues<ThemeGroup>())
        {
            bool any = false;
            foreach (var (slot, row) in _rows.Where(row => row.Slot.Group == group))
            {
                bool match = query.Length == 0 || slot.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase);
                row.Visibility = match ? Visibility.Visible : Visibility.Collapsed;
                any |= match;
            }

            _headers[group].Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
