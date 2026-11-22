using Microsoft.UI.Dispatching;
using Quill.Editor.Services;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>
/// Runs render work on the thread pool and completes it on the UI thread (05). A failed job
/// completes with null, which the views show as their placeholder.
/// </summary>
public sealed class DispatcherRenderScheduler : IRenderScheduler
{
    private readonly DispatcherQueue _dispatcher;

    public DispatcherRenderScheduler(DispatcherQueue dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Schedule(Func<CancellationToken, Task<object?>> work, Action<object?> completed)
    {
        _ = Task.Run(async () =>
        {
            object? result;
            try
            {
                result = await work(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                result = null;
            }

            _dispatcher.TryEnqueue(() => completed(result));
        });
    }
}
