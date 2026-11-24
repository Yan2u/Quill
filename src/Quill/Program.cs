using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Quill.Settings.Taskbar;

namespace Quill;

/// <summary>
/// Custom entry point (replaces the XAML-generated Main via <c>DISABLE_XAML_GENERATED_MAIN</c>) so
/// Jump List and Explorer launches forward directly into an existing instance of this executable
/// before initializing WinUI 3.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string[] commandLine = Environment.GetCommandLineArgs();
        var request = LaunchArguments.Parse(commandLine, includesExecutable: true);

        SingleInstanceServer? instanceServer = null;
        if (request.Kind != LaunchActionKind.VerifyStartup)
        {
            string executablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Quill.exe");
            instanceServer = SingleInstanceBridge.AcquireOrForward(executablePath, request, out bool forwarded);
            if (forwarded)
            {
                return;
            }
        }

        using (instanceServer)
        {
            App.InstanceServer = instanceServer;
            WinRT.ComWrappersSupport.InitializeComWrappers();
            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                _ = new App();
            });
            App.ShutdownTaskbar();
        }
    }
}
