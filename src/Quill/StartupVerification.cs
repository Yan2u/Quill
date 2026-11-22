using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace Quill;

/// <summary>
/// Off-screen startup check invoked via <c>--verify-startup &lt;reportPath&gt;</c> during release
/// packaging so compiled WinUI XAML/PRI resources and all shell pages are verified without
/// taking focus or mutating persisted user state.
/// </summary>
internal static partial class StartupVerification
{
    private const int GwlExStyle = -20;
    private const long WsExNoActivate = 0x08000000;
    private const long WsExToolWindow = 0x00000080;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int WindowWidth = 1120;
    private const int WindowHeight = 760;

    public static bool TryRun(string[] commandLine)
    {
        int index = Array.IndexOf(commandLine, "--verify-startup");
        if (index < 0 || index + 1 >= commandLine.Length)
        {
            return false;
        }

        string reportPath = Path.GetFullPath(commandLine[index + 1]);
        Run(reportPath);
        return true;
    }

    private static void Run(string reportPath)
    {
        nint foregroundBefore = GetForegroundWindow();
        Exception? recordedFailure = null;
        Application.Current.UnhandledException += (_, args) =>
        {
            recordedFailure ??= args.Exception ?? new InvalidOperationException(args.Message);
            args.Handled = true;
        };

        try
        {
            var window = new MainWindow(path: null, applyStartup: false, headlessVerification: true);
            nint hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            SetWindowLongPtr(hwnd, GwlExStyle, GetWindowLongPtr(hwnd, GwlExStyle) | (nint)(WsExNoActivate | WsExToolWindow));
            try
            {
                window.AppWindow.IsShownInSwitchers = false;
            }
            catch (NotImplementedException)
            {
            }

            int left = GetSystemMetrics(SmXVirtualScreen) - WindowWidth - 64;
            int top = GetSystemMetrics(SmYVirtualScreen);
            window.AppWindow.MoveAndResize(new RectInt32(left, top, WindowWidth, WindowHeight));
            window.AppWindow.Show(activateWindow: false);

            bool started = false;
            var fallback = window.DispatcherQueue.CreateTimer();
            fallback.Interval = TimeSpan.FromMilliseconds(250);
            fallback.IsRepeating = false;

            async void ExecuteVerification()
            {
                if (started)
                {
                    return;
                }

                started = true;
                fallback.Stop();
                try
                {
                    await Task.Delay(150);
                    window.VerifyAllPagesForStartup();
                    await Task.Delay(150);

                    bool foregroundUnchanged = GetForegroundWindow() == foregroundBefore;
                    if (recordedFailure is not null)
                    {
                        WriteFailure(reportPath, recordedFailure);
                        Environment.Exit(1);
                        return;
                    }

                    if (!foregroundUnchanged)
                    {
                        WriteReport(reportPath, $"FAIL: Foreground window changed during startup verification.{Environment.NewLine}foregroundUnchanged=False{Environment.NewLine}");
                        Environment.Exit(1);
                        return;
                    }

                    WriteReport(reportPath, $"OK{Environment.NewLine}foregroundUnchanged=True{Environment.NewLine}");
                    window.Close();
                    Application.Current.Exit();
                }
                catch (Exception ex)
                {
                    WriteFailure(reportPath, ex);
                    Environment.Exit(1);
                }
            }

            window.VerificationRoot.Loaded += (_, _) => ExecuteVerification();
            fallback.Tick += (_, _) => ExecuteVerification();
            fallback.Start();
        }
        catch (Exception ex)
        {
            WriteFailure(reportPath, ex);
            Environment.Exit(1);
        }
    }

    private static void WriteFailure(string reportPath, Exception ex) =>
        WriteReport(reportPath, $"FAIL: {ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8}){Environment.NewLine}{ex}{Environment.NewLine}");

    private static void WriteReport(string reportPath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
}
