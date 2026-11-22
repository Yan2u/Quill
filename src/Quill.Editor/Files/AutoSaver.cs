namespace Quill.Editor.Files;

/// <summary>
/// Auto-save (M6.4, off by default; a setting of the future settings system): a document with a file
/// saves <see cref="Delay"/> after the last edit, and at once when the window loses the focus or
/// closes. The save itself runs through the host (snapshot, background serialization, 09 rule 5).
/// The timer runs on <see cref="TimeProvider"/>; its callback goes through <c>dispatch</c> to the UI thread.
/// </summary>
public sealed class AutoSaver : IDisposable
{
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(1);

    private readonly Func<bool> _canSave;
    private readonly Func<Task> _save;
    private readonly Action<Action> _dispatch;
    private readonly ITimer _timer;

    /// <param name="canSave">Whether there is something to save: a file path and unsaved changes.</param>
    /// <param name="save">Saves the document (on the UI thread).</param>
    /// <param name="dispatch">Runs an action on the UI thread.</param>
    public AutoSaver(Func<bool> canSave, Func<Task> save, Action<Action> dispatch, TimeProvider? time = null, TimeSpan? delay = null)
    {
        _canSave = canSave;
        _save = save;
        _dispatch = dispatch;
        Delay = delay ?? DefaultDelay;
        _timer = (time ?? TimeProvider.System).CreateTimer(_ => _dispatch(() => _ = SaveNowAsync()), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public bool IsEnabled { get; set; }

    public TimeSpan Delay { get; }

    /// <summary>The document changed: save after <see cref="Delay"/> without further edits.</summary>
    public void NotifyEdited()
    {
        if (IsEnabled)
        {
            _timer.Change(Delay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>The window lost the focus or closes: save now.</summary>
    public Task NotifyLeavingAsync()
    {
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return SaveNowAsync();
    }

    public void Dispose() => _timer.Dispose();

    private Task SaveNowAsync() => IsEnabled && _canSave() ? _save() : Task.CompletedTask;
}
