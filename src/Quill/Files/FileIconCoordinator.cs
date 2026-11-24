using System.Security;
using Quill.Settings.Explorer;

namespace Quill.Files;

/// <summary>
/// Schedules the passive Markdown file icon hook off the UI thread so startup and window
/// reactivation stay non-blocking and throttled.
/// </summary>
internal static class FileIconCoordinator
{
    private const long MinIntervalMs = 2000;

    private static int _running;
    private static long _lastRunTick = long.MinValue;

    public static void ScheduleSync(bool force = false)
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref _lastRunTick);
        if (!force && last != long.MinValue && now - last < MinIntervalMs)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(() =>
        {
            try
            {
                Volatile.Write(ref _lastRunTick, Environment.TickCount64);
                string? executablePath = Environment.ProcessPath;
                string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", MarkdownFileIconHook.MarkdownIconFileName);
                new MarkdownFileIconHook(new WindowsRegistry()).SyncIfDefault(executablePath, iconPath);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or IOException or ArgumentException)
            {
            }
            finally
            {
                Volatile.Write(ref _running, 0);
            }
        });
    }
}
