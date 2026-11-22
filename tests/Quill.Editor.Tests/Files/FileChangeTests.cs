using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Files;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Files;

/// <summary>
/// M6.4: external changes are confirmed by content and never reported for the editor's own writes;
/// a reload is one undo item; auto-save waits for a pause in editing and saves when leaving.
/// </summary>
public sealed class FileChangeTests : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "quill-tests", Guid.NewGuid().ToString("N"));

    public FileChangeTests() => Directory.CreateDirectory(_folder);

    private string File1 => Path.Combine(_folder, "a.md");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static async Task<FileChangeKind?> NextChange(FileChangeMonitor monitor, Func<Task> act, TimeSpan timeout)
    {
        var raised = new TaskCompletionSource<FileChangeKind>(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.Changed += (_, kind) => raised.TrySetResult(kind);
        await act();
        var done = await Task.WhenAny(raised.Task, Task.Delay(timeout));
        return done == raised.Task ? raised.Task.Result : null;
    }

    [Fact]
    public async Task AnotherProgramsEditIsReported()
    {
        File.WriteAllText(File1, "one");
        using var monitor = new FileChangeMonitor(File1, TimeSpan.FromMilliseconds(50));
        Assert.Equal(FileChangeKind.Modified, await NextChange(monitor, () => File.WriteAllTextAsync(File1, "two"), Wait));
    }

    [Fact]
    public async Task ADeletedFileIsReported()
    {
        File.WriteAllText(File1, "one");
        using var monitor = new FileChangeMonitor(File1, TimeSpan.FromMilliseconds(50));
        Assert.Equal(FileChangeKind.Deleted, await NextChange(monitor, () => { File.Delete(File1); return Task.CompletedTask; }, Wait));
    }

    [Fact]
    public async Task TheEditorsOwnSaveIsNotReported()
    {
        File.WriteAllText(File1, "one");
        using var monitor = new FileChangeMonitor(File1, TimeSpan.FromMilliseconds(50));
        var document = MarkdownImporter.Import("saved by the editor", new SequentialBlockIdSource()).Document;
        var change = await NextChange(monitor, async () =>
        {
            monitor.BeginOwnWrite();
            await MarkdownFile.SaveAsync(document, File1);
            monitor.EndOwnWrite();
        }, TimeSpan.FromSeconds(1));
        Assert.Null(change);
    }

    [Fact]
    public async Task RewritingTheSameContentIsNotReported()
    {
        File.WriteAllText(File1, "same");
        using var monitor = new FileChangeMonitor(File1, TimeSpan.FromMilliseconds(50));
        Assert.Null(await NextChange(monitor, () => File.WriteAllTextAsync(File1, "same"), TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void AReloadIsOneUndoItem()
    {
        var host = EditorTestHost.Load("first\n\nsecond<|>");
        host.Type(" edited");
        var reloaded = MarkdownImporter.Import("first\n\nchanged on disk", host.Ids).Document;
        host.Execute(new ReloadDocument(reloaded));
        Assert.Equal("first\n\nchanged on disk", TestMarkdown.Serialize(host.State.Document));
        // The caret keeps its leaf and offset (13, after "second edited"), clamped to the new text.
        Assert.Equal("first\n\nchanged on di<|>sk", host.Dump());

        host.Press("Ctrl+Z");
        Assert.Equal("first\n\nsecond edited", TestMarkdown.Serialize(host.State.Document));
        host.Press("Ctrl+Y");
        Assert.Equal("first\n\nchanged on disk", TestMarkdown.Serialize(host.State.Document));
    }

    [Fact]
    public void AReloadKeepsTheFormatOfTheFileRead()
    {
        var host = EditorTestHost.Load("<|>a");
        var reloaded = MarkdownImporter.ImportFile([0xEF, 0xBB, 0xBF, (byte)'b', (byte)'\r', (byte)'\n'], host.Ids).Document;
        host.Execute(new ReloadDocument(reloaded));
        Assert.True(host.State.Document.Format is { HasByteOrderMark: true, LineEnding: Documents.LineEnding.CrLf });
        host.Press("Ctrl+Z");
        Assert.False(host.State.Document.Format.HasByteOrderMark);
    }

    [Fact]
    public async Task AutoSaveWaitsForAPauseAndSavesWhenLeaving()
    {
        var time = new ManualTimeProvider();
        int saves = 0;
        bool modified = true;
        using var saver = new AutoSaver(() => modified, () => { saves++; return Task.CompletedTask; }, action => action(), time) { IsEnabled = true };

        saver.NotifyEdited();
        time.Advance(TimeSpan.FromMilliseconds(600));
        saver.NotifyEdited();
        time.Advance(TimeSpan.FromMilliseconds(600));
        Assert.Equal(0, saves);
        time.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Equal(1, saves);

        await saver.NotifyLeavingAsync();
        Assert.Equal(2, saves);

        modified = false;
        await saver.NotifyLeavingAsync();
        saver.IsEnabled = false;
        modified = true;
        await saver.NotifyLeavingAsync();
        saver.NotifyEdited();
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(2, saves);
    }

    /// <summary>A clock whose timers fire only when the test advances it.</summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly List<ManualTimer> _timers = [];
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        public void Advance(TimeSpan time)
        {
            _now += time;
            foreach (var timer in _timers.ToList())
            {
                timer.FireIfDue(_now);
            }
        }

        private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
        {
            private DateTimeOffset? _due;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                _due = dueTime == Timeout.InfiniteTimeSpan ? null : owner._now + dueTime;
                return true;
            }

            public void FireIfDue(DateTimeOffset now)
            {
                if (_due is { } due && due <= now)
                {
                    _due = null;
                    callback(state);
                }
            }

            public void Dispose() => _due = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
