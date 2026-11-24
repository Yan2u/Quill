using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.WinUI;
using Quill.Files;
using Quill.Editor.WinUI.Input;
using Quill.Editor.WinUI.Localization;
using Quill.Settings.Taskbar;

namespace Quill;

/// <summary>
/// The application window (M5.6): the <see cref="MarkdownBox"/> in the window's frame (MainWindow.Shell.cs),
/// whose title bar shows the file name and whether it was modified, with a File menu to open and save
/// documents; New opens another window. Files keep
/// their line breaks, encoding and byte order mark (06); a save commits the paragraph at the caret
/// first and writes the snapshot in the background (09). Unsaved changes are never lost without asking.
/// </summary>
public sealed partial class MainWindow : Window
{
    private const string AppName = "Quill";

    private readonly MarkdownBox _editor = new();
    private readonly SemaphoreSlim _saving = new(1, 1);
    private string? _path;
    private bool _closeConfirmed;
    private bool _asking;
    private bool _initialLaunchHandled;
    private bool _closed;
    private readonly bool _headlessVerification;
    private readonly EditorMenuBar _menus;

    /// <param name="path">The file to open, or null for an empty document.</param>
    /// <param name="applyStartup">Whether to follow the startup option (the first window without a file, Q113).</param>
    /// <param name="headlessVerification">True during off-screen startup verification so state and window geometry are not persisted.</param>
    public MainWindow(string? path, bool applyStartup = false, bool headlessVerification = false)
        : this(
            path is null
                ? new LaunchRequest(LaunchActionKind.NewWindow)
                : new LaunchRequest(LaunchActionKind.OpenPath, path),
            applyStartup,
            headlessVerification)
    {
    }

    internal MainWindow(LaunchRequest launchRequest, bool applyStartup = false, bool headlessVerification = false)
    {
        _headlessVerification = headlessVerification;
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        InitializeTitleBarColors();
        InitializeShell();
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _closed = true;

        EditorHost.Children.Add(_editor);

        // The find panel floats over the editor (12-find-replace.md); it opens on Ctrl+F and Ctrl+H.
        EditorHost.Children.Add(new Quill.Editor.WinUI.Search.FindPanel(_editor));
        _menus = new EditorMenuBar(AppMenuBar, _editor);
        _editor.Changed += (_, _) => UpdateTitle();
        _editor.LinkActivated += async (_, link) => await OpenLinkAsync(link);
        InitializeImeTrace();
        InitializeFiles();
        Show(NewDocument(out var ids), ids, null);
        InitializeView();
        InitializeSettings();
        InitializeLocalization();
        InitializeInsertions();
        RestoreState();

        if (!_headlessVerification)
        {
            Activated += OnWindowActivated;
        }

        if (launchRequest.Kind is LaunchActionKind.OpenFile or LaunchActionKind.OpenFolder or LaunchActionKind.OpenPath)
        {
            RootGrid.Loaded += async (_, _) =>
            {
                if (_initialLaunchHandled)
                {
                    return;
                }

                _initialLaunchHandled = true;
                await OpenLaunchRequestAsync(launchRequest);
            };
        }
        else if (applyStartup)
        {
            RootGrid.Loaded += async (_, _) =>
            {
                if (_initialLaunchHandled)
                {
                    return;
                }

                _initialLaunchHandled = true;
                await ApplyStartupAsync();
            };
        }
    }

    internal string? CurrentFilePath => _path;

    internal string? CurrentFolderPath => _files.OpenedFolder;

    internal bool CanReuseForLaunch =>
        !_headlessVerification &&
        _path is null &&
        _files.OpenedFolder is null &&
        !_editor.IsModified &&
        CurrentPage == Shell.Pages.ShellPage.Editor &&
        _editor.HasDocument &&
        _editor.State.Document.Children is { Count: 0 } or [Paragraph { Text.Length: 0 }];

    internal void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        Activate();
        _ = SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    internal void LaunchTarget(LaunchRequest request)
    {
        if (RootGrid.IsLoaded)
        {
            _ = OpenLaunchRequestAsync(request);
            return;
        }

        void OnLoaded(object sender, RoutedEventArgs args)
        {
            RootGrid.Loaded -= OnLoaded;
            _ = OpenLaunchRequestAsync(request);
        }

        RootGrid.Loaded += OnLoaded;
    }

    private async Task OpenLaunchRequestAsync(LaunchRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TargetPath))
        {
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.TargetPath.Trim());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.fileMissing", request.TargetPath));
            return;
        }

        var resolvedKind = request.Kind == LaunchActionKind.OpenPath
            ? (Directory.Exists(fullPath) ? LaunchActionKind.OpenFolder : LaunchActionKind.OpenFile)
            : request.Kind;

        if (resolvedKind == LaunchActionKind.OpenFolder)
        {
            string fullFolder = Path.TrimEndingDirectorySeparator(fullPath);
            if (!Directory.Exists(fullFolder))
            {
                bool wasRecent = App.RecentFolders.RemoveRange([fullFolder]);
                await FileDialogs.ShowErrorAsync(
                    RootGrid.XamlRoot,
                    UiStrings.Format(wasRecent ? "dialog.recentFolderMissing" : "dialog.folderMissing", fullFolder));
                return;
            }

            ShowFolder(fullFolder);
            return;
        }

        if (resolvedKind == LaunchActionKind.OpenFile)
        {
            if (!File.Exists(fullPath))
            {
                bool wasRecent = App.RecentFiles.RemoveRange([fullPath]);
                await FileDialogs.ShowErrorAsync(
                    RootGrid.XamlRoot,
                    UiStrings.Format(wasRecent ? "dialog.recentMissing" : "dialog.fileMissing", fullPath));
                return;
            }

            if (await ConfirmSwitchAsync())
            {
                ShowPage(Shell.Pages.ShellPage.Editor);
                await OpenFileAsync(fullPath);
            }
        }
    }

    /// <summary>The file name shown in the title, "Untitled" before the first save.</summary>
    private string DisplayName => _path is null ? UiStrings.Get("window.untitled") : Path.GetFileName(_path);

    private static Document NewDocument(out IBlockIdSource ids)
    {
        ids = new SequentialBlockIdSource();

        // New documents are UTF-8 without byte order mark, ending with a line break, with the line break of the settings (Q46, Q103).
        return MarkdownImporter.Import(string.Empty, ids).Document with { Format = NewDocumentFormat() };
    }

    private void Show(Document document, IBlockIdSource ids, string? path)
    {
        _path = path;
        _editor.DocumentDirectory = path is null ? null : Path.GetDirectoryName(path);
        _editor.Load(EditorState.AtStart(document), ids);
        UpdateTitle();
        _menus?.Refresh();
        EncodingMenu.IsEnabled = path is not null;
        WatchFile(path);
        _files.ShowDocument(path);
        _refresh?.Start();
        StateChanged();
        _editor.Focus(FocusState.Programmatic);
    }

    private void UpdateTitle()
    {
        // The bar shows the name with a dot when modified; the window title (taskbar) keeps the star.
        string title = $"{(_editor.IsModified ? "*" : string.Empty)}{DisplayName} - {AppName}";
        string shown = _editor.IsModified ? $"{DisplayName} •" : DisplayName;
        if (Title != title)
        {
            TitleText.Text = shown;
            ToolTipService.SetToolTip(TitleText, _path ?? DisplayName);
            Title = title;
        }
    }

    private async Task OpenFileAsync(string path, TextEncodingChoice? encoding = null)
    {
        try
        {
            var ids = new SequentialBlockIdSource();
            var result = await MarkdownFile.OpenAsync(path, ids, encoding: encoding);
            _chosenEncoding = encoding;
            Show(result.Document, ids, path);
            if (!_headlessVerification)
            {
                App.RecentFiles.Add(path);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.openFailed", path, error.Message));
        }
    }

    /// <summary>Saves to the document's file, or asks for one; returns whether the document was saved.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (!await _saving.WaitAsync(0))
        {
            return false;
        }

        try
        {
            string? path = _path;
            if (saveAs || path is null)
            {
                path = await FileDialogs.PickSaveAsync(this, DisplayName, Settings.FileExtension);
                if (path is null)
                {
                    return false;
                }
            }

            var snapshot = _editor.PrepareSave();
            DocumentFormat written;

            // The monitor of this file must not take the editor's own write for another program's.
            var monitor = _path == path ? _monitor : null;
            monitor?.BeginOwnWrite();
            try
            {
                written = await MarkdownFile.SaveAsync(snapshot.Document, path);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                await FileDialogs.ShowErrorAsync(RootGrid.XamlRoot, UiStrings.Format("dialog.saveFailed", path, error.Message));
                return false;
            }
            finally
            {
                monitor?.EndOwnWrite();
            }

            if (_path != path)
            {
                _path = path;
                _editor.DocumentDirectory = Path.GetDirectoryName(path);
                EncodingMenu.IsEnabled = true;
                WatchFile(path);
                _files.ShowDocument(path);
                if (!_headlessVerification)
                {
                    App.RecentFiles.Add(path);
                }

                StateChanged();
            }

            _editor.MarkSaved(snapshot);
            UpdateTitle();
            await WarnEncodingChangedAsync(snapshot.Document.Format, written);
            return true;
        }
        finally
        {
            _saving.Release();
        }
    }

    /// <summary>
    /// Before another file replaces the document in this window (14: auto-save when switching files, Q104):
    /// a document with a file is saved when the setting is on (always while auto-save is on), otherwise the
    /// user is asked; an untitled one is always asked about. Returns whether to go on.
    /// </summary>
    private async Task<bool> ConfirmSwitchAsync()
    {
        if (_editor.IsModified && _path is not null && Settings.SavesOnSwitch)
        {
            return await SaveAsync(saveAs: false);
        }

        return await ConfirmDiscardAsync();
    }

    /// <summary>Before the document is replaced or the window closes: asks about unsaved changes; returns whether to go on.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (!_editor.IsModified)
        {
            return true;
        }

        if (_asking)
        {
            return false;
        }

        _asking = true;
        try
        {
            return await FileDialogs.AskUnsavedAsync(RootGrid.XamlRoot, DisplayName) switch
            {
                UnsavedChoice.Save => await SaveAsync(saveAs: false),
                UnsavedChoice.Discard => true,
                _ => false,
            };
        }
        finally
        {
            _asking = false;
            _editor.Focus(FocusState.Programmatic);
        }
    }

    private async Task OpenLinkAsync(LinkTarget link)
    {
        if (Uri.TryCreate(link.Destination, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "mailto")
        {
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        RecordWindowSize();
        if (_closeConfirmed || !_editor.IsModified)
        {
            return;
        }

        args.Cancel = true;

        // With auto-save on, a document with a file is saved instead of asking.
        if (_autoSave is { IsEnabled: true } && _path is not null)
        {
            await _autoSave.NotifyLeavingAsync();
        }

        if (_editor.IsModified && AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }

        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    /// <summary>A new, empty document in a new window (Typora).</summary>
    private void OnNewClick(object sender, RoutedEventArgs e) => App.OpenWindow(null);

    private async void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmSwitchAsync() && await FileDialogs.PickOpenAsync(this) is { } path)
        {
            await OpenFileAsync(path);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e) => ShowPage(Shell.Pages.ShellPage.Export);

    private async void OnOpenFolderClick(object sender, RoutedEventArgs e) => await OpenFolderAsync();

    private async void OnSaveClick(object sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnSaveAsClick(object sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async void OnExitClick(object sender, RoutedEventArgs e)
    {
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    private static void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            FileIconCoordinator.ScheduleSync();
        }
    }

    private void InitializeImeTrace()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("QUILL_IME_TRACE"), "1", StringComparison.Ordinal))
        {
            return;
        }

        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppName);
        Directory.CreateDirectory(folder);
        string tracePath = Path.Combine(folder, $"ime-trace-{Environment.ProcessId}.txt");
        var gate = new Lock();
        var writer = new StreamWriter(new FileStream(tracePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
        {
            AutoFlush = true,
        };
        Closed += (_, _) =>
        {
            lock (gate)
            {
                writer.Dispose();
            }
        };
        _editor.TextInputTrace = line =>
        {
            lock (gate)
            {
                try
                {
                    writer.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {line}");
                }
                catch (ObjectDisposedException)
                {
                }
                catch (IOException)
                {
                }
            }
        };
    }

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
