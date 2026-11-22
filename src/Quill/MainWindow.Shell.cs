using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Quill.Editor.Commands;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;
using Quill.Files;
using Quill.Shell.Pages;

namespace Quill;

/// <summary>
/// The window's frame (13-app-shell.md, after the Codex app): a 48 px title bar that is also the toolbar
/// (navigation, undo, redo, the sidebar, the menus, the file name), the navigation at the left (Home,
/// Open, Theme; About and Settings at the bottom) and the content card with its rounded corner and
/// shadow, which shows the editor or a page.
/// </summary>
public sealed partial class MainWindow
{
    private readonly Dictionary<ShellPage, UIElement> _shellPages = [];
    private bool _navigationExpanded;
    private bool _narrow;
    private bool _selecting;
    private bool _xamlRootSubscribed;

    /// <summary>The page shown, for tests.</summary>
    internal ShellPage CurrentPage { get; private set; } = ShellPage.Editor;

    /// <summary>The root container, for off-screen startup verification.</summary>
    internal FrameworkElement VerificationRoot => RootGrid;

    /// <summary>Cycles through all shell pages so startup verification exercises every XAML view.</summary>
    internal void VerifyAllPagesForStartup()
    {
        ShowPage(ShellPage.Open);
        ShowPage(ShellPage.Export);
        ShowPage(ShellPage.Theme);
        ShowPage(ShellPage.Settings);
        ShowPage(ShellPage.About);
        ShowPage(ShellPage.Editor);
    }

    private void InitializeShell()
    {
        string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Quill.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        string navIconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Quill-nav.svg");
        if (File.Exists(navIconPath))
        {
            var svg = new SvgImageSource(new Uri(navIconPath));
            svg.OpenFailed += (_, _) =>
            {
                string fallbackPng = Path.Combine(AppContext.BaseDirectory, "Assets", "Quill-256.png");
                if (File.Exists(fallbackPng))
                {
                    HomeIcon.Source = new BitmapImage(new Uri(fallbackPng));
                }
                else
                {
                    HomeItem.Icon = new FontIcon { Glyph = "\uE80F" };
                }
            };
            HomeIcon.Source = svg;
        }
        else
        {
            HomeItem.Icon = new FontIcon { Glyph = "\uE80F" };
        }

        RestoreWindowSize();

        SetTitleBar(DragRegion);
        RootGrid.Loaded += (_, _) =>
        {
            if (!_xamlRootSubscribed && RootGrid.XamlRoot is { } root)
            {
                _xamlRootSubscribed = true;
                root.Changed += (_, _) => UpdateTitleBarInsets();
            }

            UpdateTitleBarInsets();
            RecordWindowSize();
        };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidSizeChange || args.DidPresenterChange)
            {
                UpdateTitleBarInsets();
                RecordWindowSize();
                StateChanged();
            }
        };

        NavView.SelectedItem = HomeItem;
        RootGrid.SizeChanged += (_, args) => UpdateNavigationMode(args.NewSize.Width);

        // Pages come in as in a NavigationView's frame: rising a little and fading in.
        PageHost.ChildrenTransitions = [new EntranceThemeTransition { FromHorizontalOffset = 0, FromVerticalOffset = 28, IsStaggeringEnabled = false }];

        // The card floats over the backdrop: a theme shadow cast by a plain element behind it (not by the card,
        // see ShadowCaster in MainWindow.xaml) onto the area behind both.
        var shadow = new ThemeShadow();
        shadow.Receivers.Add(ShadowReceiver);
        ShadowCaster.Shadow = shadow;
        ShadowCaster.Translation = new System.Numerics.Vector3(0, 0, 16);

        SidebarSizer.TargetControl = SidebarHost;
        _editor.Changed += (_, _) => UpdateUndoButtons();
        UpdateUndoButtons();
        UpdateEditorMenuBarVisibility();
    }

    private void UpdateEditorMenuBarVisibility() =>
        AppMenuBar.Visibility = Quill.Shell.WindowChrome.EditorMenuBarVisibility(CurrentPage);

    /// <summary>Shows the editor or a page in the content card; pages are built when first shown.</summary>
    private void ShowPage(ShellPage page)
    {
        CurrentPage = page;
        UpdateEditorMenuBarVisibility();
        SelectNavigationItem(page);
        if (page == ShellPage.Editor)
        {
            PageHost.Children.Clear();
            PageHost.Visibility = Visibility.Collapsed;
            EditorPage.Visibility = Visibility.Visible;
            _editor.Focus(FocusState.Programmatic);
            return;
        }

        if (!_shellPages.TryGetValue(page, out var content))
        {
            content = CreatePage(page);
            _shellPages[page] = content;
        }

        if (content is OpenPage open)
        {
            open.SetRecent(_recent.Paths);
        }

        PageHost.Children.Clear();
        PageHost.Children.Add(content);
        EditorPage.Visibility = Visibility.Collapsed;
        PageHost.Visibility = Visibility.Visible;
    }

    private UIElement CreatePage(ShellPage page)
    {
        switch (page)
        {
            case ShellPage.Open:
                var open = new OpenPage();
                open.OpenRequested += async (_, _) =>
                {
                    if (await ConfirmSwitchAsync() && await FileDialogs.PickOpenAsync(this) is { } path)
                    {
                        ShowPage(ShellPage.Editor);
                        await OpenFileAsync(path);
                    }
                };
                open.RecentRequested += async (_, path) =>
                {
                    ShowPage(ShellPage.Editor);
                    await OpenRecentAsync(path);
                };
                open.ClearRequested += (_, _) =>
                {
                    _recent.Clear();
                    open.SetRecent(_recent.Paths);
                };
                return open;
            case ShellPage.Export:
                return new ExportPage();
            case ShellPage.Theme:
                var themes = new Shell.Themes.ThemesPage(App.Themes, App.Settings);
                themes.ThemeOpened += (_, theme) => ShowThemeEditor(theme);
                return themes;
            case ShellPage.Settings:
                var settings = new Shell.Settings.SettingsPage(App.Settings, ThemeChoices())
                {
                    FolderPicker = () => FileDialogs.PickFolderAsync(this),
                    NewMenu = new Quill.Settings.Explorer.ExplorerNewMenu(new Quill.Settings.Explorer.WindowsRegistry()),
                };
                settings.ThemesRequested += (_, _) => ShowPage(ShellPage.Theme);
                return settings;
            default:
                string aboutIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "Quill-256.png");
                return new AboutPage(AboutVersion(), aboutIcon);
        }
    }

    private static string AboutVersion() => typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    /// <summary>The configuration page of a theme, under the Theme item (15-themes.md); Back returns to the list.</summary>
    private void ShowThemeEditor(Shell.Themes.ThemeInfo theme)
    {
        var editor = new Shell.Themes.ThemeEditorPage(App.Themes, theme);
        editor.BackRequested += (_, _) => ShowPage(ShellPage.Theme);
        editor.Copied += (_, copy) => ShowThemeEditor(copy);
        CurrentPage = ShellPage.Theme;
        UpdateEditorMenuBarVisibility();
        SelectNavigationItem(ShellPage.Theme);
        PageHost.Children.Clear();
        PageHost.Children.Add(editor);
        EditorPage.Visibility = Visibility.Collapsed;
        PageHost.Visibility = Visibility.Visible;
    }

    /// <summary>Marks the page's item as selected; Export (opened from the File menu) has none.</summary>
    private void SelectNavigationItem(ShellPage page)
    {
        var item = NavView.MenuItems.Concat(NavView.FooterMenuItems).OfType<NavigationViewItem>()
            .FirstOrDefault(item => item.Tag is string tag && tag == page.ToString());
        if (!ReferenceEquals(NavView.SelectedItem, item))
        {
            _selecting = true;
            NavView.SelectedItem = item;
            _selecting = false;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (!_selecting && args.SelectedItemContainer?.Tag is string tag && Enum.TryParse(tag, out ShellPage page))
        {
            ShowPage(page);
        }
    }

    /// <summary>The title bar's button: labels in and out on a wide window, the drawer on a narrow one.</summary>
    private void OnNavigationClick(object sender, RoutedEventArgs e)
    {
        if (_narrow)
        {
            NavView.IsPaneOpen = !NavView.IsPaneOpen;
            return;
        }

        _navigationExpanded = !_navigationExpanded;
        NavView.IsPaneOpen = _navigationExpanded;
        StateChanged();
    }

    /// <summary>Icons (or icons and labels) at the left on a wide window; a drawer below <see cref="Quill.Shell.WindowChrome.NarrowNavigationWidth"/>.</summary>
    private void UpdateNavigationMode(double width)
    {
        if (Quill.Shell.WindowChrome.TryGetNarrowNavigation(width, Quill.Shell.WindowChrome.IsMinimized(AppWindow)) is not { } narrow)
        {
            return;
        }

        if (narrow == _narrow && NavView.PaneDisplayMode == (narrow ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.Left))
        {
            return;
        }

        _narrow = narrow;
        NavView.PaneDisplayMode = narrow ? NavigationViewPaneDisplayMode.LeftMinimal : NavigationViewPaneDisplayMode.Left;
        NavView.IsPaneOpen = !narrow && _navigationExpanded;
    }

    /// <summary>Keeps the bar's content clear of the caption buttons (and of a left inset in right-to-left layouts).</summary>
    private void UpdateTitleBarInsets()
    {
        if (RootGrid.XamlRoot is not { } root)
        {
            return;
        }

        var bar = AppWindow.TitleBar;
        if (Quill.Shell.WindowChrome.TryGetTitleBarInsets(
            bar.LeftInset,
            bar.RightInset,
            root.RasterizationScale,
            Quill.Shell.WindowChrome.IsMinimized(AppWindow)) is not var (left, right))
        {
            return;
        }

        LeftInsetColumn.Width = left;
        RightInsetColumn.Width = right;
    }

    private void UpdateUndoButtons()
    {
        if (!_editor.HasDocument)
        {
            return;
        }

        var history = _editor.MenuContext().History;
        UndoButton.IsEnabled = history.CanUndo;
        RedoButton.IsEnabled = history.CanRedo;
    }

    private void OnUndoClick(object sender, RoutedEventArgs e)
    {
        _editor.Execute(new Undo());
        _editor.Focus(FocusState.Programmatic);
    }

    private void OnRedoClick(object sender, RoutedEventArgs e)
    {
        _editor.Execute(new Redo());
        _editor.Focus(FocusState.Programmatic);
    }

    private void OnSidebarClick(object sender, RoutedEventArgs e)
    {
        ShowPage(ShellPage.Editor);
        ShowSidebar(!IsSidebarShown);
    }
}
