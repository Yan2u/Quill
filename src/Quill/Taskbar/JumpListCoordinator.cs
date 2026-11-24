using Quill.Editor.Files;
using Quill.Editor.WinUI.Localization;
using Quill.Settings.Taskbar;

namespace Quill.Taskbar;

/// <summary>
/// Maintains Quill's Windows taskbar Jump List on a dedicated background STA thread, coalescing
/// rapid changes to recent files, recent folders, and display language, and pruning entries the
/// user explicitly removed from the Jump List.
/// </summary>
internal sealed class JumpListCoordinator : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(150);

    private readonly RecentFileList _recentFiles;
    private readonly RecentFolderList _recentFolders;
    private readonly IJumpListBackend _backend;
    private readonly TaskbarJumpList _jumpList;
    private readonly string _executablePath;
    private readonly JumpListIcon _fileIcon;
    private readonly JumpListIcon _appIcon;
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly ManualResetEventSlim _disposeSignal = new(false);
    private readonly Thread _worker;

    private JumpListIcon? _folderIcon;
    private int _suppressHistoryEvents;
    private int _dirty;
    private int _flush;
    private int _disposing;
    private int _disposed;

    public JumpListCoordinator(
        RecentFileList recentFiles,
        RecentFolderList recentFolders,
        IJumpListBackend? backend = null)
    {
        _recentFiles = recentFiles ?? throw new ArgumentNullException(nameof(recentFiles));
        _recentFolders = recentFolders ?? throw new ArgumentNullException(nameof(recentFolders));
        _backend = backend ?? new WindowsJumpListBackend();
        _jumpList = new TaskbarJumpList(_backend);

        string baseDir = AppContext.BaseDirectory;
        _executablePath = Environment.ProcessPath ?? Path.Combine(baseDir, "Quill.exe");

        string markdownIco = Path.Combine(baseDir, "Assets", "Quill-Markdown.ico");
        string appIco = Path.Combine(baseDir, "Assets", "Quill.ico");
        _fileIcon = new JumpListIcon(File.Exists(markdownIco) ? markdownIco : _executablePath, 0);
        _appIcon = new JumpListIcon(File.Exists(appIco) ? appIco : _executablePath, 0);

        _recentFiles.Changed += OnHistoryChanged;
        _recentFolders.Changed += OnHistoryChanged;
        UiStrings.LanguageChanged += OnLanguageChanged;

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "Quill.JumpList",
        };
        _worker.SetApartmentState(ApartmentState.STA);
        _worker.Start();
    }

    public void RequestSync()
    {
        if (Volatile.Read(ref _disposing) != 0 || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        Volatile.Write(ref _dirty, 1);
        _signal.Set();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        if (Volatile.Read(ref _suppressHistoryEvents) == 0)
        {
            RequestSync();
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RequestSync();

    private void WorkerLoop()
    {
        while (true)
        {
            _signal.Wait();
            _signal.Reset();

            if (Volatile.Read(ref _flush) == 0)
            {
                _ = _disposeSignal.Wait(DebounceDelay);
            }

            if (Interlocked.Exchange(ref _dirty, 0) != 0)
            {
                RunSyncOnce();
            }

            if (Volatile.Read(ref _disposing) != 0 && Volatile.Read(ref _dirty) == 0)
            {
                break;
            }
        }
    }

    private void RunSyncOnce()
    {
        _folderIcon ??= _backend.ResolveFolderIcon();

        var labels = new JumpListLabels(
            RecentFoldersTitle: UiStrings.Get("jumplist.recentFolders"),
            RecentFilesTitle: UiStrings.Get("jumplist.recentFiles"),
            NewWindowTitle: UiStrings.Get("jumplist.newWindow"),
            NewWindowDescription: UiStrings.Get("jumplist.newWindowDesc"));

        var input = new JumpListSyncInput(
            ExecutablePath: _executablePath,
            FileIcon: _fileIcon,
            AppIcon: _appIcon,
            FolderIcon: _folderIcon,
            RecentFolders: _recentFolders.Paths,
            RecentFiles: _recentFiles.Paths,
            Labels: labels);

        var result = _jumpList.Sync(input);
        if (result.RemovedFiles.Count == 0 && result.RemovedFolders.Count == 0)
        {
            return;
        }

        Interlocked.Increment(ref _suppressHistoryEvents);
        try
        {
            if (result.RemovedFiles.Count > 0)
            {
                _recentFiles.RemoveRange(result.RemovedFiles);
            }

            if (result.RemovedFolders.Count > 0)
            {
                _recentFolders.RemoveRange(result.RemovedFolders);
            }
        }
        finally
        {
            Interlocked.Decrement(ref _suppressHistoryEvents);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _recentFiles.Changed -= OnHistoryChanged;
        _recentFolders.Changed -= OnHistoryChanged;
        UiStrings.LanguageChanged -= OnLanguageChanged;

        Volatile.Write(ref _flush, 1);
        Volatile.Write(ref _disposing, 1);
        _disposeSignal.Set();
        _signal.Set();

        if (_worker.Join(TimeSpan.FromMilliseconds(1000)))
        {
            _signal.Dispose();
            _disposeSignal.Dispose();
        }
    }
}
