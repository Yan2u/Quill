using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Quill.Editor.Files;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;
using Quill.Settings.Taskbar;
using Quill.Taskbar;

namespace Quill;

/// <summary>
/// The application (M5.6): opens files or folders from the command line or taskbar Jump List.
/// New opens another window in the same process; the process ends with its last window. Every window
/// shares one settings store, state store, and recent file/folder lists (14-settings.md).
/// </summary>
public partial class App : Application
{
    private static readonly List<MainWindow> Windows = [];
    private static DispatcherQueue? _uiDispatcher;
    private static JumpListCoordinator? _jumpList;

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        SyncLanguage(Settings.Current.Language);
    }

    internal static SingleInstanceServer? InstanceServer { get; set; }

    internal static void SyncLanguage(DisplayLanguage language) =>
        UiStrings.SetLanguage(language == DisplayLanguage.Chinese ? UiLanguage.Chinese : UiLanguage.English);

    private static void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill");
            Directory.CreateDirectory(folder);
            string logPath = Path.Combine(folder, "crash.log");
            string hr = e.Exception is { } ex ? $"0x{ex.HResult:X8}" : "null";
            string detail = e.Exception?.ToString() ?? "<null>";
            string entry = $"[{DateTimeOffset.Now:O}] HRESULT={hr} Message={e.Message}{Environment.NewLine}{detail}{Environment.NewLine}{Environment.NewLine}";
            File.AppendAllText(logPath, entry);
        }
        catch
        {
        }
    }

    /// <summary>The settings of every window (<c>settings.json</c>).</summary>
    public static SettingsStore Settings { get; } = new(SettingsStore.DefaultPath);

    /// <summary>The application state (<c>state.json</c>): the sidebar, the navigation, the last folder and file.</summary>
    public static AppStateStore State { get; } = new(AppStateStore.DefaultPath);

    /// <summary>The recently opened files shared by every window and the taskbar Jump List.</summary>
    public static RecentFileList RecentFiles { get; } = new(RecentFileList.DefaultStorage);

    /// <summary>The recently opened folders shared by every window and the taskbar Jump List.</summary>
    public static RecentFolderList RecentFolders { get; } = new(RecentFolderList.DefaultStorage);

    /// <summary>The editor themes of every window (15-themes.md): built-in ones and the user's.</summary>
    public static Quill.Shell.Themes.ThemeCatalog Themes { get; } = new(new Quill.Settings.Themes.ThemeStore(Quill.Settings.Themes.ThemeStore.DefaultFolder));

    internal static void ShutdownTaskbar()
    {
        _jumpList?.Dispose();
        _jumpList = null;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        var request = LaunchArguments.Parse(commandLine, includesExecutable: true);
        if (request.Kind == LaunchActionKind.VerifyStartup && StartupVerification.TryRun(commandLine))
        {
            return;
        }

        Files.FileIconCoordinator.ScheduleSync(force: true);

        _uiDispatcher = DispatcherQueue.GetForCurrentThread();
        _jumpList ??= new JumpListCoordinator(RecentFiles, RecentFolders);
        _jumpList.RequestSync();

        InstanceServer?.SetHandler(forwarded => _uiDispatcher?.TryEnqueue(() => HandleExternalLaunch(forwarded)));

        // A file or folder on the command line comes before the startup option (Q113).
        _ = OpenLaunchWindow(request, applyStartup: request.Kind == LaunchActionKind.Default);
    }

    /// <summary>
    /// Opens a window with the file at <paramref name="path"/>, or an empty document; only the first window
    /// follows the startup option, new windows are always empty (Q113).
    /// </summary>
    public static void OpenWindow(string? path, bool applyStartup = false)
    {
        var request = path is null
            ? new LaunchRequest(LaunchActionKind.NewWindow)
            : new LaunchRequest(LaunchActionKind.OpenPath, path);
        _ = OpenLaunchWindow(request, applyStartup);
    }

    internal static MainWindow OpenLaunchWindow(LaunchRequest request, bool applyStartup = false)
    {
        var window = new MainWindow(request, applyStartup);
        Windows.Add(window);
        window.Closed += (_, _) =>
        {
            Windows.Remove(window);
            if (Windows.Count == 0)
            {
                ShutdownTaskbar();
            }
        };
        window.Activate();
        return window;
    }

    private static void HandleExternalLaunch(LaunchRequest request)
    {
        if (request.Kind is LaunchActionKind.NewWindow or LaunchActionKind.Default ||
            string.IsNullOrWhiteSpace(request.TargetPath))
        {
            var newWindow = OpenLaunchWindow(new LaunchRequest(LaunchActionKind.NewWindow), applyStartup: false);
            newWindow.BringToFront();
            return;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(request.TargetPath.Trim());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            var errorWindow = Windows.FirstOrDefault(static w => w.CanReuseForLaunch)
                ?? OpenLaunchWindow(new LaunchRequest(LaunchActionKind.NewWindow), applyStartup: false);
            errorWindow.BringToFront();
            errorWindow.LaunchTarget(request);
            return;
        }

        var effectiveKind = request.Kind == LaunchActionKind.OpenPath
            ? (Directory.Exists(fullPath) ? LaunchActionKind.OpenFolder : LaunchActionKind.OpenFile)
            : request.Kind;

        if (effectiveKind == LaunchActionKind.OpenFolder)
        {
            string fullFolder = Path.TrimEndingDirectorySeparator(fullPath);
            if (Windows.FirstOrDefault(w =>
                    w.CurrentFolderPath is { } openFolder &&
                    string.Equals(Path.TrimEndingDirectorySeparator(openFolder), fullFolder, StringComparison.OrdinalIgnoreCase)) is { } existingFolderWindow)
            {
                existingFolderWindow.BringToFront();
                return;
            }

            if (Windows.FirstOrDefault(static w => w.CanReuseForLaunch) is { } reusableForFolder)
            {
                reusableForFolder.BringToFront();
                reusableForFolder.LaunchTarget(new LaunchRequest(LaunchActionKind.OpenFolder, fullFolder));
                return;
            }

            var folderWindow = OpenLaunchWindow(new LaunchRequest(LaunchActionKind.OpenFolder, fullFolder), applyStartup: false);
            folderWindow.BringToFront();
            return;
        }

        if (Windows.FirstOrDefault(w =>
                w.CurrentFilePath is { } openFile &&
                string.Equals(openFile, fullPath, StringComparison.OrdinalIgnoreCase)) is { } existingFileWindow)
        {
            existingFileWindow.BringToFront();
            return;
        }

        if (Windows.FirstOrDefault(static w => w.CanReuseForLaunch) is { } reusableForFile)
        {
            reusableForFile.BringToFront();
            reusableForFile.LaunchTarget(new LaunchRequest(LaunchActionKind.OpenFile, fullPath));
            return;
        }

        var fileWindow = OpenLaunchWindow(new LaunchRequest(LaunchActionKind.OpenFile, fullPath), applyStartup: false);
        fileWindow.BringToFront();
    }
}
