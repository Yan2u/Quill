using System.Security.Cryptography;

namespace Quill.Editor.Files;

/// <summary>What happened to a watched file.</summary>
public enum FileChangeKind
{
    /// <summary>Its content differs from what the editor last read or wrote.</summary>
    Modified,

    /// <summary>It no longer exists (deleted, or renamed away).</summary>
    Deleted,
}

/// <summary>
/// Watches the file of the open document for changes made by other programs (M6.4). Changes are
/// confirmed by content: after file events settle, the file's hash is compared with the content the
/// editor last read or wrote (<see cref="Acknowledge"/>), so touching a file or the editor's own save
/// (bracketed by <see cref="BeginOwnWrite"/> and <see cref="EndOwnWrite"/>) raises nothing. A file still
/// locked by its writer is checked again a moment later. <see cref="Changed"/> is raised on a thread-pool
/// thread.
/// </summary>
public sealed class FileChangeMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _timer;
    private readonly TimeSpan _settle;
    private byte[]? _known;
    private bool _knownMissing;
    private int _ownWrites;
    private bool _disposed;

    public FileChangeMonitor(string path, TimeSpan? settle = null)
    {
        Path = System.IO.Path.GetFullPath(path);
        _settle = settle ?? TimeSpan.FromMilliseconds(300);
        _timer = new Timer(_ => Check(), null, Timeout.Infinite, Timeout.Infinite);
        Acknowledge();
        _watcher = new FileSystemWatcher(System.IO.Path.GetDirectoryName(Path)!, System.IO.Path.GetFileName(Path))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
        };
        _watcher.Changed += (_, _) => Schedule();
        _watcher.Created += (_, _) => Schedule();
        _watcher.Deleted += (_, _) => Schedule();
        _watcher.Renamed += (_, _) => Schedule();
        _watcher.Error += (_, _) => Schedule();
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised when the file's content changed or the file disappeared; on a thread-pool thread.</summary>
    public event EventHandler<FileChangeKind>? Changed;

    public string Path { get; }

    /// <summary>Takes the file's current content as the editor's (after opening, reloading or keeping the editor's version).</summary>
    public void Acknowledge()
    {
        var hash = HashOf(Path, out bool missing);
        lock (_gate)
        {
            _known = hash;
            _knownMissing = missing;
        }
    }

    /// <summary>The editor starts writing the file: changes are not reported until <see cref="EndOwnWrite"/>.</summary>
    public void BeginOwnWrite()
    {
        lock (_gate)
        {
            _ownWrites++;
        }
    }

    /// <summary>The editor finished writing: what it wrote is the known content.</summary>
    public void EndOwnWrite()
    {
        Acknowledge();
        lock (_gate)
        {
            _ownWrites = Math.Max(0, _ownWrites - 1);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
        }

        _watcher.Dispose();
        _timer.Dispose();
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _timer.Change(_settle, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void Check()
    {
        lock (_gate)
        {
            if (_disposed || _ownWrites > 0)
            {
                return;
            }
        }

        byte[]? hash;
        bool missing;
        try
        {
            hash = HashOf(Path, out missing, throwWhenLocked: true);
        }
        catch (IOException)
        {
            // Still being written by another program: look again later.
            Schedule();
            return;
        }

        FileChangeKind? change = null;
        lock (_gate)
        {
            if (_disposed || _ownWrites > 0)
            {
                return;
            }

            if (missing && !_knownMissing)
            {
                change = FileChangeKind.Deleted;
            }
            else if (!missing && (_knownMissing || _known is null || !hash!.AsSpan().SequenceEqual(_known)))
            {
                change = FileChangeKind.Modified;
            }

            // Reported once: the new state is known from now on.
            _known = hash;
            _knownMissing = missing;
        }

        if (change is { } kind)
        {
            Changed?.Invoke(this, kind);
        }
    }

    private static byte[]? HashOf(string path, out bool missing, bool throwWhenLocked = false)
    {
        missing = false;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return SHA256.HashData(stream);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            missing = true;
            return null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException && !throwWhenLocked)
        {
            return null;
        }
    }
}
