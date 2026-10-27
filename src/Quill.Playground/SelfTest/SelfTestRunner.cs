using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Runs the in-process self-tests when the app is started with <c>--selftest [reportPath]</c>.
/// The tests need a real XAML tree (layout, focus, XamlReader), so they run inside the app
/// on the UI thread, write a plain-text report, then shut the app down.
/// </summary>
internal static class SelfTestRunner
{
    private static readonly (string Name, Func<SelfTestContext, Task> Run)[] Groups =
    [
        ("Platform probes", PlatformProbes.RunAsync),
        ("Markup parser", MarkupParserTests.RunAsync),
        ("Reconciler", ReconcilerTests.RunAsync),
        ("Virtual stack", VirtualStackTests.RunAsync),
        ("Crash repro", CrashRepro.RunAsync),
    ];

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

        bool started = false;
        stage.Loaded += async (_, _) =>
        {
            if (started)
            {
                return;
            }

            started = true;
            var context = new SelfTestContext(window, stage) { TracePath = reportPath + ".trace.txt" };
            File.Delete(context.TracePath);
            foreach (var (name, run) in Groups)
            {
                if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (only is null && name == "Crash repro")
                {
                    // Opt-in only: a reproduced crash ends the process.
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
            }

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, context.BuildReport());
            Application.Current.Exit();
        };

        window.Activate();
    }
}
