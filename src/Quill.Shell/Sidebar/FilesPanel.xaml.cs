using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Quill.Editor.Files;
using Quill.Editor.WinUI.Localization;

namespace Quill.Shell.Sidebar;

/// <summary>How the Files panel shows a folder.</summary>
public enum FilesView
{
    Tree,
    List,
}

/// <summary>
/// The Files panel of the sidebar (13-app-shell.md): the opened folder, or the folder of the document when
/// none was opened. The tree shows subfolders and Markdown files and wraps long names; the list shows the
/// Markdown files of the folder itself and cuts long names short. Both follow changes on disk; reading the
/// folder again updates the entries in place, so the expanded folders, the scroll position and the
/// selection stay. A click asks the window to open the file, which is then selected.
/// </summary>
public sealed partial class FilesPanel : UserControl, IDisposable
{
    private static readonly TimeSpan RefreshDelay = TimeSpan.FromMilliseconds(300);

    private readonly DispatcherQueueTimer _refresh;
    private FileSystemWatcher? _watcher;
    private string? _openedFolder;
    private string? _documentPath;
    private string? _loadedRoot;
    private bool _isActive = true;
    private bool _dirty;

    public FilesPanel()
    {
        InitializeComponent();
        _refresh = DispatcherQueue.CreateTimer();
        _refresh.Interval = RefreshDelay;
        _refresh.IsRepeating = false;
        _refresh.Tick += (_, _) => Refresh();
        ApplyLanguage();
        Loaded += (_, _) =>
        {
            UiStrings.LanguageChanged -= OnLanguageChanged;
            UiStrings.LanguageChanged += OnLanguageChanged;
            ApplyLanguage();
        };
        Unloaded += (_, _) =>
        {
            if (!IsLoaded)
            {
                UiStrings.LanguageChanged -= OnLanguageChanged;
            }
        };
        Rebuild();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => ApplyLanguage();

    internal void RefreshLanguage() => ApplyLanguage();

    private void ApplyLanguage()
    {
        string openFolder = UiStrings.Get("sidebar.files.openFolder");
        OpenFolderText.Text = openFolder;
        ToolTipService.SetToolTip(OpenFolderButton, openFolder);
        AutomationProperties.SetName(OpenFolderButton, openFolder);
        AutomationProperties.SetName(ViewSwitch, UiStrings.Get("sidebar.files.view"));
        string tree = UiStrings.Get("sidebar.files.tree");
        ToolTipService.SetToolTip(TreeTab, tree);
        AutomationProperties.SetName(TreeTab, tree);
        string list = UiStrings.Get("sidebar.files.list");
        ToolTipService.SetToolTip(ListTab, list);
        AutomationProperties.SetName(ListTab, list);
        UpdateVisibility();
    }

    /// <summary>A Markdown file was clicked.</summary>
    public event EventHandler<string>? FileInvoked;

    /// <summary>"Open folder" was clicked: the window asks for a folder and calls <see cref="OpenFolder"/>.</summary>
    public event EventHandler? OpenFolderRequested;

    /// <summary>The user switched between the tree and the list.</summary>
    public event EventHandler? ViewChanged;

    /// <summary>The entries of the tree's top level.</summary>
    public ObservableCollection<FileEntry> Roots { get; } = [];

    /// <summary>The rows of the list.</summary>
    public ObservableCollection<FileEntry> Files { get; } = [];

    /// <summary>The folder shown, or null when there is none.</summary>
    public string? RootFolder => CurrentRoot();

    /// <summary>The folder opened with <see cref="OpenFolder"/>, or null when the panel follows the document.</summary>
    public string? OpenedFolder => _openedFolder;

    /// <summary>When false, directory scans and the file system watcher pause until shown again.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            if (value)
            {
                if (_dirty || !string.Equals(RootFolder, _loadedRoot, StringComparison.OrdinalIgnoreCase) || (_watcher is null && RootFolder is not null))
                {
                    Rebuild();
                }
            }
            else
            {
                _refresh.Stop();
                _watcher?.Dispose();
                _watcher = null;
                _dirty = true;
            }
        }
    }

    public FilesView View
    {
        get => ViewSwitch.SelectedIndex == 1 ? FilesView.List : FilesView.Tree;
        set => ViewSwitch.SelectedIndex = value == FilesView.List ? 1 : 0;
    }

    /// <summary>
    /// The document was shown or saved under a new name: it is selected, and its folder shown unless one was
    /// opened. In the folder already shown nothing is read again and the tree stays as it is.
    /// </summary>
    public void ShowDocument(string? path)
    {
        _documentPath = path;
        if (!_isActive)
        {
            _dirty = true;
            return;
        }

        if (string.Equals(CurrentRoot(), _loadedRoot, StringComparison.OrdinalIgnoreCase) && (_loadedRoot is null || Roots.Count > 0))
        {
            Select();
            return;
        }

        Rebuild();
    }

    /// <summary>Shows a folder until another is opened.</summary>
    public void OpenFolder(string folder)
    {
        _openedFolder = Path.GetFullPath(folder);
        if (!_isActive)
        {
            _dirty = true;
            return;
        }

        Rebuild();
    }

    /// <summary>Reads the folder again.</summary>
    public void Refresh()
    {
        if (!_isActive)
        {
            _dirty = true;
            return;
        }

        Rebuild();
    }

    public void Dispose()
    {
        _refresh.Stop();
        _watcher?.Dispose();
        _watcher = null;
    }

    /// <summary>Reads a folder's children, once; a test or the tree calls this when it expands.</summary>
    internal static void Realize(FileEntry folder)
    {
        if (!folder.HasUnrealizedChildren)
        {
            return;
        }

        folder.Children.Clear();
        foreach (var entry in Read(folder.Path))
        {
            folder.Children.Add(entry);
        }

        folder.HasUnrealizedChildren = false;
    }

    /// <summary>Raises <see cref="FileInvoked"/> for a file; the tree and the list call this, and tests.</summary>
    internal void Invoke(FileEntry entry)
    {
        if (!entry.IsFolder)
        {
            FileInvoked?.Invoke(this, entry.Path);
        }
    }

    private static IEnumerable<FileEntry> Read(string folder)
    {
        var content = MarkdownFiles.Read(folder);
        return content.Folders.Select(static path => new FileEntry(path, isFolder: true))
            .Concat(content.Files.Select(static path => new FileEntry(path, isFolder: false)));
    }

    /// <summary>
    /// Makes <paramref name="target"/> show <paramref name="fresh"/> in order, keeping the entries that are
    /// still there (with their expansion, and the tree's containers and scroll position); the folders that
    /// were read are read again.
    /// </summary>
    private static void Sync(ObservableCollection<FileEntry> target, IReadOnlyList<FileEntry> fresh)
    {
        var kept = new Dictionary<(string, bool), FileEntry>();
        foreach (var entry in target)
        {
            kept.TryAdd((entry.Path.ToUpperInvariant(), entry.IsFolder), entry);
        }

        for (int i = 0; i < fresh.Count; i++)
        {
            if (!kept.Remove((fresh[i].Path.ToUpperInvariant(), fresh[i].IsFolder), out var entry))
            {
                target.Insert(i, fresh[i]);
                continue;
            }

            int at = target.IndexOf(entry);
            if (at != i)
            {
                target.Move(at, i);
            }

            if (entry.IsFolder && !entry.HasUnrealizedChildren)
            {
                Sync(entry.Children, Read(entry.Path).ToList());
            }
        }

        while (target.Count > fresh.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    private static FileEntry? Find(IEnumerable<FileEntry> entries, string path)
    {
        foreach (var entry in entries)
        {
            if (string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }

            if (entry.IsExpanded && Find(entry.Children, path) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private string? CurrentRoot() => _openedFolder ?? (_documentPath is null ? null : Path.GetDirectoryName(_documentPath));

    private void Rebuild()
    {
        string? root = CurrentRoot();
        if (!string.Equals(root, _loadedRoot, StringComparison.OrdinalIgnoreCase))
        {
            // Another folder: nothing to keep.
            Watch(root);
            Roots.Clear();
            Files.Clear();
        }
        else if (_watcher is null && root is not null)
        {
            Watch(root);
        }

        _loadedRoot = root;
        _dirty = false;
        bool exists = root is not null && Directory.Exists(root);
        FolderText.Text = exists ? Path.GetFileName(root!.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } name ? name : root : string.Empty;
        ToolTipService.SetToolTip(FolderText, exists ? root : null);
        FolderText.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
        var entries = exists ? Read(root!).ToList() : [];
        Sync(Roots, entries);
        Sync(Files, [.. entries.Where(static entry => !entry.IsFolder).Select(static entry => new FileEntry(entry.Path, isFolder: false))]);
        Select();
        UpdateVisibility();
    }

    /// <summary>Selects the document in the tree (when its folder is expanded) and in the list.</summary>
    private void Select()
    {
        var inTree = _documentPath is { } document ? Find(Roots, document) : null;
        var inList = _documentPath is { } path ? Find(Files, path) : null;
        if (!ReferenceEquals(Tree.SelectedItem, inTree))
        {
            Tree.SelectedItem = inTree;
        }

        if (!ReferenceEquals(List.SelectedItem, inList))
        {
            List.SelectedItem = inList;
        }
    }

    private void UpdateVisibility()
    {
        bool list = View == FilesView.List;
        Tree.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        List.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        bool empty = list ? Files.Count == 0 : Roots.Count == 0;
        EmptyText.Text = RootFolder is null ? UiStrings.Get("sidebar.files.noFolder") : UiStrings.Get("sidebar.files.noFiles");
        EmptyText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Follows changes in the folder and below it; a burst of them refreshes once.</summary>
    private void Watch(string? folder)
    {
        _watcher?.Dispose();
        _watcher = null;
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            var watcher = new FileSystemWatcher(folder)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
            };
            watcher.Created += OnFolderChanged;
            watcher.Deleted += OnFolderChanged;
            watcher.Renamed += OnFolderChanged;
            watcher.Error += (_, _) => ScheduleRefresh();
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Some locations cannot be watched: the panel still shows what it read.
        }
    }

    private void OnFolderChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh() => DispatcherQueue.TryEnqueue(() =>
    {
        _refresh.Stop();
        _refresh.Start();
    });

    /// <summary>
    /// Shows an item's chevron by its own entry, not by what the reused container showed before: a file has
    /// none, and its node is told it has no unrealized children (the tree's own state may say otherwise).
    /// </summary>
    private void OnItemDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        if (sender is TreeViewItem item && args.NewValue is FileEntry entry)
        {
            if (Tree.NodeFromContainer(item) is { } node && node.HasUnrealizedChildren != entry.HasUnrealizedChildren)
            {
                node.HasUnrealizedChildren = entry.HasUnrealizedChildren;
            }

            UpdateGlyph(item, entry);
        }
    }

    private static void UpdateGlyph(TreeViewItem item, FileEntry entry) =>
        item.GlyphOpacity = entry.IsFolder && (entry.HasUnrealizedChildren || entry.Children.Count > 0) ? 1 : 0;

    private void OnExpanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        switch (args.Item)
        {
            case FileEntry { IsFolder: true } folder:
                Realize(folder);
                if (folder.Children.Count == 0 && sender.ContainerFromItem(folder) is TreeViewItem empty)
                {
                    UpdateGlyph(empty, folder);
                }

                break;
            case FileEntry:
                // A file has nothing to expand, whatever state its node took over.
                args.Node.HasUnrealizedChildren = false;
                args.Node.IsExpanded = false;
                break;
        }
    }

    private void OnItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is FileEntry entry)
        {
            Invoke(entry);
        }
    }

    private void OnListItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is FileEntry entry)
        {
            Invoke(entry);
        }
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);

    private void OnViewSwitchChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateVisibility();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }
}
