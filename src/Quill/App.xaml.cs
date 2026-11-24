using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Localization;
using Quill.Settings;

namespace Quill;

/// <summary>
/// The application (M5.6): the first command line argument, if any, is the file to open. New opens
/// another window in the same process; the process ends with its last window. Every window shares one
/// settings store and one state store (14-settings.md), so a change in one window shows in the others.
/// </summary>
public partial class App : Application
{
    private static readonly List<Window> Windows = [];

    public App()
    {
        UnhandledException += OnUnhandledException;
        InitializeComponent();
        SyncLanguage(Settings.Current.Language);
    }

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

    /// <summary>The editor themes of every window (15-themes.md): built-in ones and the user's.</summary>
    public static Quill.Shell.Themes.ThemeCatalog Themes { get; } = new(new Quill.Settings.Themes.ThemeStore(Quill.Settings.Themes.ThemeStore.DefaultFolder));

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        if (StartupVerification.TryRun(commandLine))
        {
            return;
        }

        Files.FileIconCoordinator.ScheduleSync(force: true);

        string? path = commandLine.Length > 1 ? commandLine[1] : null;

        // A file on the command line comes before the startup option (Q113).
        OpenWindow(path, applyStartup: path is null);
    }

    /// <summary>
    /// Opens a window with the file at <paramref name="path"/>, or an empty document; only the first window
    /// follows the startup option, new windows are always empty (Q113).
    /// </summary>
    public static void OpenWindow(string? path, bool applyStartup = false)
    {
        var window = new MainWindow(path, applyStartup);
        Windows.Add(window);
        window.Closed += (_, _) => Windows.Remove(window);
        window.Activate();
    }
}
