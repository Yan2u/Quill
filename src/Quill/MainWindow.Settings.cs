using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Quill.Editor;
using Quill.Editor.Documents;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Theming;
using Quill.Files;
using Quill.Settings;
using Quill.Shell;
using Quill.Shell.Sidebar;
using Windows.Graphics;

namespace Quill;

/// <summary>
/// The settings and the application state in the window (14-settings.md). Settings apply at once in
/// every window: the app theme, the editor's light and dark themes, the code blocks' wrapping, line
/// numbers and math, the editing options (brackets, copying), the status bar, the reading speed,
/// auto-save; others are read when used (the save dialog's extension, the line break of
/// new documents, saving when switching files). The sidebar, the navigation and the last folder and file
/// are written to the state as they change; the startup option is applied to the first window only.
/// </summary>
public sealed partial class MainWindow
{
    private static readonly TimeSpan StateDelay = TimeSpan.FromMilliseconds(400);

    private DispatcherQueueTimer? _stateTimer;
    private bool _restoringState;
    private SizeInt32? _restoredWindowSize;
    private bool? _windowMaximized;

    private static AppSettings Settings => App.Settings.Current;

    private void InitializeSettings()
    {
        App.Settings.Changed += OnSettingsChanged;
        App.Themes.Changed += OnThemesChanged;
        Closed += (_, _) =>
        {
            App.Settings.Changed -= OnSettingsChanged;
            App.Themes.Changed -= OnThemesChanged;
        };
        ApplySettings(Settings);
    }

    /// <summary>A theme was copied, saved or deleted: the editor shows its themes again (a saved theme in use applies at once, 15).</summary>
    private void OnThemesChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplySettings(Settings);
            (_shellPages.GetValueOrDefault(Shell.Pages.ShellPage.Settings) as Shell.Settings.SettingsPage)?.SetThemes(ThemeChoices());
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => OnThemesChanged(sender, e));
        }
    }

    /// <summary>The themes the settings can choose.</summary>
    private static IReadOnlyList<Shell.Settings.ThemeChoice> ThemeChoices() =>
        [.. App.Themes.Themes.Select(static theme => new Shell.Settings.ThemeChoice(theme.Id, theme.Name))];

    private void OnSettingsChanged(object? sender, StoreChangedEventArgs<AppSettings> e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplySettings(e.Current);
        }
        else
        {
            DispatcherQueue.TryEnqueue(() => ApplySettings(App.Settings.Current));
        }
    }

    /// <summary>Shows the settings that take effect at once; none of them projects the document again (05).</summary>
    private void ApplySettings(AppSettings settings)
    {
        App.SyncLanguage(settings.Language);
        RootGrid.RequestedTheme = settings.AppTheme switch
        {
            AppTheme.Light => ElementTheme.Light,
            AppTheme.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };

        // A theme that no longer exists falls back to the default one.
        _editor.LightStyleSheet = App.Themes.Resolve(settings.LightTheme, BuiltInThemes.Github);
        _editor.DarkStyleSheet = App.Themes.Resolve(settings.DarkTheme, BuiltInThemes.GithubDarkDimmed);
        var view = new MdViewOptions { WrapCode = settings.WrapCode, CodeLineNumbers = settings.CodeLineNumbers, CodeBlockMath = settings.CodeBlockMath };
        if (view != _editor.ViewOptions)
        {
            _editor.ViewOptions = view;
        }

        _editor.Options = EditorOptionsOf(settings);

        StatusBarHost.Visibility = settings.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;
        _wordCount.WordsPerMinute = settings.ReadingSpeed;
        if (_autoSave is not null && _autoSave.IsEnabled != settings.AutoSave)
        {
            _autoSave.IsEnabled = settings.AutoSave;
            _autoSave.NotifyEdited();
        }

        AutoSaveItem.IsChecked = settings.AutoSave;
    }

    /// <summary>The settings that change editing, as the core takes them (01: the core never reads the settings).</summary>
    private static EditorOptions EditorOptionsOf(AppSettings settings) => EditorOptions.Default with
    {
        MatchBrackets = settings.MatchBrackets,
        CopyMarkdownSource = settings.CopyMarkdownSource,
        CopyLineWithoutSelection = settings.CopyLineWithoutSelection,
    };

    /// <summary>The format of a new document: its line break comes from the settings (Q103).</summary>
    private static DocumentFormat NewDocumentFormat() =>
        DocumentFormat.ForNewDocument(Settings.DefaultLineBreak == LineBreakChoice.Lf ? LineEnding.Lf : LineEnding.CrLf);

    /// <summary>Restores the sidebar and the navigation as they were left.</summary>
    private void RestoreState()
    {
        var state = App.State.Current;
        _restoringState = true;
        try
        {
            SidebarHost.Width = state.SidebarWidth;
            _files.View = state.FilesView == FilesViewState.List ? FilesView.List : FilesView.Tree;
            if (_sidebar is not null)
            {
                _sidebar.Tab = state.SidebarTab == SidebarTabState.Files ? SidebarTab.Files : SidebarTab.Outline;
            }

            if (state.SidebarVisible)
            {
                ShowSidebar(true);
            }

            _navigationExpanded = state.NavigationExpanded;
            NavView.IsPaneOpen = !_narrow && _navigationExpanded;
        }
        finally
        {
            _restoringState = false;
        }

        _stateTimer = DispatcherQueue.CreateTimer();
        _stateTimer.Interval = StateDelay;
        _stateTimer.IsRepeating = false;
        _stateTimer.Tick += (_, _) => SaveState();
        SidebarHost.SizeChanged += (_, _) => StateChanged();
        if (_sidebar is not null)
        {
            _sidebar.TabChanged += (_, _) => StateChanged();
        }

        _files.ViewChanged += (_, _) => StateChanged();
        Closed += (_, _) =>
        {
            _stateTimer.Stop();
            SaveState();
        };
    }

    /// <summary>The sidebar, the navigation or the folder changed: written a moment later (a drag changes the width many times).</summary>
    private void StateChanged()
    {
        if (!_restoringState)
        {
            _stateTimer?.Start();
        }
    }

    private void RestoreWindowSize()
    {
        if (_headlessVerification)
        {
            return;
        }

        var state = App.State.Current;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        if (WindowChrome.TryGetRestoredWindowSize(state.WindowWidth, state.WindowHeight, workArea, AppState.MinWindowWidth, AppState.MinWindowHeight) is { } size)
        {
            AppWindow.Resize(size);
            _restoredWindowSize = size;
        }

        if (state.WindowMaximized && AppWindow.Presenter is OverlappedPresenter presenter)
        {
            _windowMaximized = true;
            presenter.Maximize();
        }
    }

    private void RecordWindowSize()
    {
        if (_headlessVerification)
        {
            return;
        }

        if (WindowChrome.IsMaximized(AppWindow))
        {
            _windowMaximized = true;
            return;
        }

        if (!WindowChrome.IsRestored(AppWindow))
        {
            return;
        }

        _windowMaximized = false;
        var size = AppWindow.Size;
        if (size.Width >= AppState.MinWindowWidth && size.Height >= AppState.MinWindowHeight)
        {
            _restoredWindowSize = size;
        }
    }

    private void SaveState()
    {
        if (_headlessVerification)
        {
            return;
        }

        App.State.Update(state => state with
        {
            SidebarVisible = IsSidebarShown,
            SidebarWidth = double.IsNaN(SidebarHost.Width) ? state.SidebarWidth : SidebarHost.Width,
            SidebarTab = _sidebar?.Tab == SidebarTab.Files ? SidebarTabState.Files : SidebarTabState.Outline,
            FilesView = _files.View == FilesView.List ? FilesViewState.List : FilesViewState.Tree,
            NavigationExpanded = _navigationExpanded,
            WindowWidth = _restoredWindowSize?.Width ?? state.WindowWidth,
            WindowHeight = _restoredWindowSize?.Height ?? state.WindowHeight,
            WindowMaximized = _windowMaximized ?? state.WindowMaximized,
            LastFolder = _files.RootFolder ?? state.LastFolder,
            LastFile = _path ?? state.LastFile,
        });
    }

    /// <summary>
    /// The startup option of the first window (14-settings.md, Q113). When the folder or the file cannot be
    /// opened, no folder is opened and a message says why.
    /// </summary>
    private async Task ApplyStartupAsync()
    {
        var settings = Settings;
        var state = App.State.Current;
        string? folder = settings.Startup switch
        {
            StartupAction.LastFolder or StartupAction.LastFolderAndFile => state.LastFolder,
            StartupAction.Folder => settings.StartupFolder,
            _ => null,
        };
        string? file = settings.Startup == StartupAction.LastFolderAndFile ? state.LastFile : null;
        if (folder is null && file is null)
        {
            return;
        }

        if (folder is not null && !Directory.Exists(folder))
        {
            await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.folderMissing", folder));
            return;
        }

        if (file is not null && !File.Exists(file))
        {
            await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.fileMissing", file));
            return;
        }

        if (folder is not null)
        {
            ShowFolder(folder);
        }

        if (file is not null)
        {
            await OpenFileAsync(file);
        }
    }
}
