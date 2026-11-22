using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;
using Quill.Settings;

namespace Quill.Shell.Themes;

/// <summary>
/// The theme list (15-themes.md): a card per theme, one column when narrow and two when wide, filtered by the
/// search box (name and description). Copy makes a user theme; Delete removes a user theme that is not in use,
/// after asking; a card opens the theme's configuration page (<see cref="ThemeOpened"/>).
/// </summary>
public sealed partial class ThemesPage : UserControl
{
    private readonly ThemeCatalog _catalog;
    private readonly SettingsStore _settings;
    private readonly TextBlock _title = new() { Style = (Style)Application.Current.Resources["TitleTextBlockStyle"] };
    private readonly TextBlock _hint = Themed.Load<TextBlock>("TextBlock", "TextWrapping='Wrap' Foreground='{ThemeResource TextFillColorSecondaryBrush}'");
    private readonly AutoSuggestBox _search = new() { QueryIcon = new SymbolIcon(Symbol.Find), MaxWidth = 480, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly ItemsRepeater _cards = new()
    {
        Layout = new UniformGridLayout
        {
            MinItemWidth = 440,
            MinItemHeight = 220,
            MinRowSpacing = 12,
            MinColumnSpacing = 12,
            MaximumRowsOrColumns = 2,
            ItemsStretch = UniformGridLayoutItemsStretch.Fill,
        },
    };

    private readonly TextBlock _empty = new() { Visibility = Visibility.Collapsed };

    public ThemesPage(ThemeCatalog catalog, SettingsStore settings)
    {
        _catalog = catalog;
        _settings = settings;
        var panel = new StackPanel { MaxWidth = 1200, Padding = new Thickness(40, 28, 40, 40), Spacing = 12, Children = { _title, _hint, _search, _cards, _empty } };
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        _search.TextChanged += (_, _) => Fill();
        Loaded += (_, _) =>
        {
            _catalog.Changed -= OnChanged;
            _catalog.Changed += OnChanged;
            _settings.Changed -= OnSettingsChanged;
            _settings.Changed += OnSettingsChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
            Fill();
        };
        Unloaded += (_, _) =>
        {
            _catalog.Changed -= OnChanged;
            _settings.Changed -= OnSettingsChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
        };
        ApplyLanguage();
        Fill();
    }

    /// <summary>A card (or its Edit / View) was clicked: the host shows the theme's configuration page.</summary>
    public event EventHandler<ThemeInfo>? ThemeOpened;

    /// <summary>Asks whether to delete a theme (its name); true to delete. A dialog by default; tests replace it.</summary>
    public Func<string, Task<bool>>? ConfirmDelete { get; set; }

    /// <summary>The cards shown, for tests.</summary>
    internal IReadOnlyList<ThemeCard> Cards => _cards.ItemsSource as IReadOnlyList<ThemeCard> ?? [];

    /// <summary>The search text; setting it filters as typing would.</summary>
    internal string Query
    {
        get => _search.Text;
        set
        {
            _search.Text = value;
            Fill();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => Fill();

    private void OnSettingsChanged(object? sender, Quill.Settings.StoreChangedEventArgs<AppSettings> e) => Fill();

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLanguage();
        Fill();
    }

    private void ApplyLanguage()
    {
        _title.Text = UiStrings.Get("themes.title");
        _hint.Text = UiStrings.Get("themes.hint");
        _search.PlaceholderText = UiStrings.Get("themes.search");
        AutomationProperties.SetName(_search, UiStrings.Get("themes.search"));
        _empty.Text = UiStrings.Get("themes.empty");
    }

    private void Fill()
    {
        string query = _search.Text.Trim();
        var cards = new List<ThemeCard>();
        foreach (var theme in _catalog.Themes)
        {
            string description = theme.IsBuiltIn
                ? BuiltInThemes.Find(theme.Id)?.LocalizedDescription ?? theme.Description
                : theme.Description;
            if (query.Length > 0 && !theme.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                && !description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            var card = new ThemeCard(theme, _catalog.CanDelete(theme.Id, _settings.Current));
            card.Opened += (_, _) => ThemeOpened?.Invoke(this, theme);
            card.CopyRequested += (_, _) => _catalog.Copy(theme.Id);
            card.DeleteRequested += async (_, _) =>
            {
                if (await (ConfirmDelete ?? AskDeleteAsync)(theme.Name))
                {
                    _catalog.Delete(theme.Id);
                }
            };
            cards.Add(card);
        }

        _cards.ItemsSource = cards;
        _empty.Visibility = cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task<bool> AskDeleteAsync(string name)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
            Title = UiStrings.Get("themes.deleteTitle"),
            Content = UiStrings.Format("themes.deleteMessage", name),
            PrimaryButtonText = UiStrings.Get("themes.deleteConfirm"),
            CloseButtonText = UiStrings.Get("dialog.cancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
