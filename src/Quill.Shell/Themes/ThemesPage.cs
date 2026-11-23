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
    private readonly List<ThemeCard> _allCards = [];
    private readonly Dictionary<string, ThemeCard> _cardsById = new(StringComparer.Ordinal);
    private UiLanguage? _appliedLanguage;

    public ThemesPage(ThemeCatalog catalog, SettingsStore settings)
    {
        _catalog = catalog;
        _settings = settings;
        var panel = new StackPanel { MaxWidth = 1200, Padding = new Thickness(40, 28, 40, 40), Spacing = 12, Children = { _title, _hint, _search, _cards, _empty } };
        Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        _search.TextChanged += (_, _) => ApplyFilter();
        Loaded += (_, _) =>
        {
            _catalog.Changed -= OnChanged;
            _catalog.Changed += OnChanged;
            _settings.Changed -= OnSettingsChanged;
            _settings.Changed += OnSettingsChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            if (_appliedLanguage != UiStrings.Language)
            {
                ApplyLanguage();
            }

            SyncCards();
        };
        Unloaded += (_, _) =>
        {
            _catalog.Changed -= OnChanged;
            _settings.Changed -= OnSettingsChanged;
            UiStrings.LanguageChanged -= OnLanguageChanged;
            if (!IsLoaded)
            {
                ReleaseCards();
            }
        };
        ApplyLanguage();
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
            ApplyFilter();
        }
    }

    private void OnChanged(object? sender, EventArgs e) => SyncCards();

    private void OnSettingsChanged(object? sender, Quill.Settings.StoreChangedEventArgs<AppSettings> e)
    {
        foreach (var card in _allCards)
        {
            card.SetCanDelete(_catalog.CanDelete(card.Theme.Id, _settings.Current));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        ApplyLanguage();
        foreach (var card in _allCards)
        {
            card.RefreshLanguage();
        }

        ApplyFilter();
    }

    private void ApplyLanguage()
    {
        _appliedLanguage = UiStrings.Language;
        _title.Text = UiStrings.Get("themes.title");
        _hint.Text = UiStrings.Get("themes.hint");
        _search.PlaceholderText = UiStrings.Get("themes.search");
        AutomationProperties.SetName(_search, UiStrings.Get("themes.search"));
        _empty.Text = UiStrings.Get("themes.empty");
    }

    private void SyncCards()
    {
        var next = new List<ThemeCard>(_catalog.Themes.Count);
        var nextById = new Dictionary<string, ThemeCard>(_catalog.Themes.Count, StringComparer.Ordinal);
        foreach (var theme in _catalog.Themes)
        {
            bool canDelete = _catalog.CanDelete(theme.Id, _settings.Current);
            if (_cardsById.TryGetValue(theme.Id, out var card))
            {
                card.Update(theme, canDelete);
            }
            else
            {
                card = CreateCard(theme, canDelete);
            }

            next.Add(card);
            nextById[theme.Id] = card;
        }

        foreach (var old in _allCards)
        {
            if (!nextById.ContainsKey(old.Theme.Id))
            {
                old.Release();
            }
        }

        _allCards.Clear();
        _allCards.AddRange(next);
        _cardsById.Clear();
        foreach (var (id, card) in nextById)
        {
            _cardsById[id] = card;
        }

        ApplyFilter();
    }

    private ThemeCard CreateCard(ThemeInfo theme, bool canDelete)
    {
        var card = new ThemeCard(theme, canDelete);
        card.Opened += (_, _) => ThemeOpened?.Invoke(this, card.Theme);
        card.CopyRequested += (_, _) => _catalog.Copy(card.Theme.Id);
        card.DeleteRequested += async (_, _) =>
        {
            if (await (ConfirmDelete ?? AskDeleteAsync)(card.Theme.Name))
            {
                _catalog.Delete(card.Theme.Id);
            }
        };
        return card;
    }

    private void ApplyFilter()
    {
        string query = _search.Text.Trim();
        var filtered = _allCards.Where(card => card.Matches(query)).ToList();
        _cards.ItemsSource = filtered;
        _empty.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ReleaseCards()
    {
        _cards.ItemsSource = null;
        foreach (var card in _allCards)
        {
            card.Release();
        }

        _allCards.Clear();
        _cardsById.Clear();
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
