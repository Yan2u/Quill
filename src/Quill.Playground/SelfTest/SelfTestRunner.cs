using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Runs the in-process self-tests when the app is started with <c>--selftest [reportPath]</c>.
/// The tests need a real XAML tree (layout, focus, XamlReader), so they run inside the app
/// on the UI thread, write a plain-text report, then shut the app down. The window is shown without
/// activation, off-screen and out of the taskbar, so a run never takes the user's foreground (Q7).
/// </summary>
internal static class SelfTestRunner
{
    private const int HostWidth = 1200;
    private const int HostHeight = 900;

    private static readonly (string Name, Func<SelfTestContext, Task> Run)[] Groups =
    [
        ("Host window", HostWindowProbes.RunAsync),
        ("Self-drawn text", SelfDrawnTextProbes.RunAsync),
        ("IME", ImeProbes.RunAsync),
        ("Editor rendering", EditorRenderProbes.RunAsync),
        ("Piecewise layout", PiecewiseLayoutProbes.RunAsync),
        ("Layout service", LayoutServiceProbes.RunAsync),
        ("Markdown box", MarkdownBoxProbes.RunAsync),
        ("Markdown box IME", MarkdownBoxImeProbes.RunAsync),
        ("Selection render", SelectionFlickerProbes.RunAsync),
        ("Adornments", AdornmentProbes.RunAsync),
        ("Formulas", FormulaProbes.RunAsync),
        ("Code highlighting", CodeHighlightProbes.RunAsync),
        ("Mermaid", MermaidProbes.RunAsync),
        ("Render polish", RenderPolishProbes.RunAsync),
        ("Code block options", CodeBlockOptionProbes.RunAsync),
        ("Image layout", ImageLayoutProbes.RunAsync),
        ("Files", FileProbes.RunAsync),
        ("Outline", OutlineProbes.RunAsync),
        ("Theme", ThemeProbes.RunAsync),
        ("Theme system", ThemeSystemProbes.RunAsync),
        ("Shell", ShellProbes.RunAsync),
        ("Settings", SettingsProbes.RunAsync),
        ("Localization", LocalizationProbes.RunAsync),
        ("Insertion", InsertionProbes.RunAsync),
        ("App integration", AppIntegrationProbes.RunAsync),
        ("Source mode", SourceModeProbes.RunAsync),
        ("Find and replace", FindReplaceProbes.RunAsync),
        ("Platform probes", PlatformProbes.RunAsync),
        ("Markup parser", MarkupParserTests.RunAsync),
        ("Reconciler", ReconcilerTests.RunAsync),
        ("Virtual stack", VirtualStackTests.RunAsync),
        ("Crash repro", CrashRepro.RunAsync),
        ("Performance", PerformanceProbes.RunAsync),
        ("Remote image (network)", ImageLayoutProbes.RunNetworkAsync),
        ("Scroll memory", ScrollMemoryProbes.RunAsync),
        ("Scroll crash", ScrollMemoryProbes.RunCrashReproAsync),
    ];

    /// <summary>Groups that run only when named with --only: a reproduced crash ends the process; the benchmarks take minutes.</summary>
    private static readonly string[] OptIn = ["Crash repro", "Performance", "Remote image (network)", "Scroll memory", "Scroll crash"];

    public static bool TryGetReportPath(string[] commandLine, out string reportPath)
    {
        int index = Array.IndexOf(commandLine, "--selftest");
        if (index < 0)
        {
            reportPath = string.Empty;
            return false;
        }

        reportPath = index + 1 < commandLine.Length
            ? Path.GetFullPath(commandLine[index + 1])
            : Path.Combine(AppContext.BaseDirectory, "selftest-report.txt");
        return true;
    }

    public static void Start(Window window, string reportPath)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        int onlyIndex = Array.IndexOf(commandLine, "--only");
        string? only = onlyIndex >= 0 && onlyIndex + 1 < commandLine.Length ? commandLine[onlyIndex + 1] : null;

        var stage = new Grid();
        window.Content = stage;
        nint foreground = MeasurementHost.ForegroundWindow();
        var cursor = MeasurementHost.CursorPosition();
        nint hostHandle = 0;

        Application.Current.UnhandledException += (_, args) =>
        {
            try
            {
                string folder = Path.GetDirectoryName(reportPath)!;
                Directory.CreateDirectory(folder);
                File.AppendAllText(reportPath + ".trace.txt",
                    $"UNHANDLED EXCEPTION [{DateTimeOffset.Now:O}]: hr=0x{args.Exception.HResult:X8} message={args.Message}{Environment.NewLine}{args.Exception}{Environment.NewLine}");
            }
            catch
            {
            }
        };

        bool started = false;
        stage.Loaded += async (_, _) =>
        {
            if (started)
            {
                return;
            }

            started = true;
            var context = new SelfTestContext(window, stage) { TracePath = reportPath + ".trace.txt", HostHandle = hostHandle };
            File.Delete(context.TracePath);
            bool everForeground = false;
            foreach (var (name, run) in Groups)
            {
                if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (only is null && OptIn.Contains(name))
                {
                    continue;
                }

                context.BeginGroup(name);
                try
                {
                    await run(context);
                }
                catch (Exception exception)
                {
                    context.Fail($"{name} (unhandled)", exception);
                }

                stage.Children.Clear();
                everForeground |= MeasurementHost.ForegroundWindow() == hostHandle;
            }

            context.BeginGroup("Foreground");
            context.Check(!everForeground, "host: the self-test window never became the foreground window");
            context.Info($"foreground window unchanged: {MeasurementHost.ForegroundWindow() == foreground}; cursor unchanged: {MeasurementHost.CursorPosition() == cursor} (the user may have moved either)");

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, context.BuildReport());
            Application.Current.Exit();
        };

        int modeIndex = Array.IndexOf(commandLine, "--host-mode");
        var mode = modeIndex >= 0 && modeIndex + 1 < commandLine.Length && Enum.TryParse(commandLine[modeIndex + 1], ignoreCase: true, out HostMode parsed)
            ? parsed
            : HostMode.OffScreen;
        hostHandle = MeasurementHost.Show(window, HostWidth, HostHeight, mode);
    }
}
