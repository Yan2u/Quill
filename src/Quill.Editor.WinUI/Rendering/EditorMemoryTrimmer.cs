using Microsoft.Graphics.Canvas;
using Microsoft.UI.Dispatching;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// Coordinates Direct2D surface trimming, grammar idle tracking, and coalesced background GC
/// compaction without retaining references to any editor instance.
/// </summary>
internal static class EditorMemoryTrimmer
{
    private static long _lastGrammarActivityTick = Environment.TickCount64;
    private static int _gcInFlight;
    private static int _gcPending;
    private static int _idleReleaseInFlight;

    public static void NotifyGrammarUsed() =>
        Volatile.Write(ref _lastGrammarActivityTick, Environment.TickCount64);

    public static bool IsGrammarIdleFor(TimeSpan minIdle) =>
        Environment.TickCount64 - Volatile.Read(ref _lastGrammarActivityTick) >= (long)minIdle.TotalMilliseconds;

    public static void ScheduleIdleGrammarRelease(DispatcherQueue? queue, TimeSpan delay)
    {
        if (Interlocked.CompareExchange(ref _idleReleaseInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                int waitMs = Math.Max(200, (int)delay.TotalMilliseconds);
                for (int attempt = 0; attempt < 4 && Quill.Highlighting.GrammarCatalog.HasLoadedCatalogs; attempt++)
                {
                    await Task.Delay(waitMs).ConfigureAwait(false);
                    if (IsGrammarIdleFor(delay))
                    {
                        if (Quill.Highlighting.GrammarCatalog.ReleaseIfUnused(delay))
                        {
                            ScheduleBackgroundGcTrim(queue);
                        }

                        break;
                    }
                }
            }
            finally
            {
                Volatile.Write(ref _idleReleaseInFlight, 0);
            }
        });
    }

    public static void TryTrimGraphicsDevice()
    {
        try
        {
            CanvasDevice.GetSharedDevice().Trim();
        }
        catch
        {
            // Ignore lost/uninitialized D2D device states during shutdown.
        }
    }

    public static void ScheduleBackgroundGcTrim(DispatcherQueue? queue)
    {
        Volatile.Write(ref _gcPending, 1);
        if (Interlocked.CompareExchange(ref _gcInFlight, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (Interlocked.Exchange(ref _gcPending, 0) == 1)
                {
                    await Task.Delay(250).ConfigureAwait(false);
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
                }
            }
            finally
            {
                Volatile.Write(ref _gcInFlight, 0);
                if (queue is not null)
                {
                    queue.TryEnqueue(TryTrimGraphicsDevice);
                }
                else
                {
                    TryTrimGraphicsDevice();
                }
            }
        });
    }
}
