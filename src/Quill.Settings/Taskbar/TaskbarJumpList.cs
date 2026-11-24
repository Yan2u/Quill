using System.Runtime.InteropServices;

namespace Quill.Settings.Taskbar;

/// <summary>
/// Builds and commits Quill's Windows taskbar Jump List: Recent Folders, Recent Files, and Tasks.
/// Respects user removals returned by <see cref="IJumpListSession.RemovedArguments"/> so categories
/// are never rejected by the shell and removed entries can be pruned from persisted history.
/// </summary>
public sealed class TaskbarJumpList(IJumpListBackend backend)
{
    private readonly IJumpListBackend _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    public JumpListSyncResult Sync(JumpListSyncInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var session = _backend.TryBeginSession(out int beginHr);
        if (session is null)
        {
            return new JumpListSyncResult(false, [], [], beginHr);
        }

        try
        {
            var (removedFiles, removedFolders, removedFileSet, removedFolderSet) = CollectRemovals(session.RemovedArguments, input);

            int maxFiles = Math.Max(0, input.MaxFiles);
            var filteredFiles = FilterPaths(input.RecentFiles, maxFiles, removedFileSet, TryNormalizeFilePath);

            int folderCap = filteredFiles.Count == 0
                ? Math.Max(Math.Max(0, input.MaxFolders), maxFiles)
                : Math.Max(0, input.MaxFolders);
            var filteredFolders = FilterPaths(input.RecentFolders, folderCap, removedFolderSet, TryNormalizeFolderPath);

            var folderIcon = input.FolderIcon ?? _backend.ResolveFolderIcon();
            int? firstNonFatalError = null;

            if (filteredFolders.Count > 0)
            {
                var folderItems = filteredFolders
                    .Select(folder => new JumpListItemSpec(
                        LaunchArguments.GetFolderDisplayName(folder),
                        folder,
                        input.ExecutablePath,
                        LaunchArguments.ForOpenFolder(folder),
                        folderIcon))
                    .ToList();

                if (!session.TryAppendCategory(new JumpListCategorySpec(input.Labels.RecentFoldersTitle, folderItems), out int folderHr))
                {
                    firstNonFatalError ??= folderHr;
                }
            }

            if (filteredFiles.Count > 0)
            {
                var fileItems = filteredFiles
                    .Select(file => new JumpListItemSpec(
                        LaunchArguments.GetFileDisplayName(file),
                        file,
                        input.ExecutablePath,
                        LaunchArguments.ForOpenFile(file),
                        input.FileIcon))
                    .ToList();

                if (!session.TryAppendCategory(new JumpListCategorySpec(input.Labels.RecentFilesTitle, fileItems), out int fileHr))
                {
                    firstNonFatalError ??= fileHr;
                }
            }

            var newWindowTask = new JumpListItemSpec(
                input.Labels.NewWindowTitle,
                input.Labels.NewWindowDescription,
                input.ExecutablePath,
                LaunchArguments.ForNewWindow(),
                input.AppIcon);

            if (!session.TryAddUserTasks([newWindowTask], out int taskHr))
            {
                firstNonFatalError ??= taskHr;
            }

            if (!session.TryCommit(out int commitHr))
            {
                session.Abort();
                return new JumpListSyncResult(false, removedFiles, removedFolders, commitHr);
            }

            return new JumpListSyncResult(true, removedFiles, removedFolders, firstNonFatalError);
        }
        catch (COMException error)
        {
            session.Abort();
            return new JumpListSyncResult(false, [], [], error.HResult);
        }
        catch
        {
            session.Abort();
            return new JumpListSyncResult(false, [], [], null);
        }
    }

    private static (
        List<string> RemovedFiles,
        List<string> RemovedFolders,
        HashSet<string> RemovedFileSet,
        HashSet<string> RemovedFolderSet) CollectRemovals(IReadOnlyList<string> removedArguments, JumpListSyncInput input)
    {
        var removedFiles = new List<string>();
        var removedFolders = new List<string>();
        var removedFileSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removedFolderSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var knownFolders = BuildNormalizedSet(input.RecentFolders, TryNormalizeFolderPath);
        var knownFiles = BuildNormalizedSet(input.RecentFiles, TryNormalizeFilePath);

        foreach (string rawArgs in removedArguments)
        {
            var request = LaunchArguments.ParseCommandLine(rawArgs);
            switch (request.Kind)
            {
                case LaunchActionKind.OpenFolder when TryNormalizeFolderPath(request.TargetPath) is { } folder:
                    if (removedFolderSet.Add(folder))
                    {
                        removedFolders.Add(folder);
                    }
                    break;

                case LaunchActionKind.OpenFile when TryNormalizeFilePath(request.TargetPath) is { } file:
                    if (removedFileSet.Add(file))
                    {
                        removedFiles.Add(file);
                    }
                    break;

                case LaunchActionKind.OpenPath when request.TargetPath is not null:
                    if (TryNormalizeFolderPath(request.TargetPath) is { } matchedFolder && knownFolders.Contains(matchedFolder))
                    {
                        if (removedFolderSet.Add(matchedFolder))
                        {
                            removedFolders.Add(matchedFolder);
                        }
                    }
                    else if (TryNormalizeFilePath(request.TargetPath) is { } matchedFile && knownFiles.Contains(matchedFile))
                    {
                        if (removedFileSet.Add(matchedFile))
                        {
                            removedFiles.Add(matchedFile);
                        }
                    }
                    break;
            }
        }

        return (removedFiles, removedFolders, removedFileSet, removedFolderSet);
    }

    private static HashSet<string> BuildNormalizedSet(IEnumerable<string> paths, Func<string?, string?> normalize)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (normalize(path) is { } normalized)
            {
                set.Add(normalized);
            }
        }

        return set;
    }

    private static List<string> FilterPaths(
        IReadOnlyList<string> paths,
        int maxCount,
        HashSet<string> removedSet,
        Func<string?, string?> normalize)
    {
        var result = new List<string>();
        if (maxCount == 0)
        {
            return result;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            if (normalize(path) is { } normalized && !removedSet.Contains(normalized) && seen.Add(normalized))
            {
                result.Add(normalized);
                if (result.Count >= maxCount)
                {
                    break;
                }
            }
        }

        return result;
    }

    internal static string? TryNormalizeFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    internal static string? TryNormalizeFolderPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
