namespace Quill.Settings.Taskbar;

/// <summary>Icon location (<c>path, index</c>) assigned to a taskbar Jump List entry.</summary>
public sealed record JumpListIcon(string Path, int Index = 0);

/// <summary>A single shell-link item in a Jump List category or Tasks list.</summary>
public sealed record JumpListItemSpec(
    string Title,
    string Description,
    string ExecutablePath,
    string Arguments,
    JumpListIcon Icon);

/// <summary>A named custom category containing Jump List items.</summary>
public sealed record JumpListCategorySpec(string Title, IReadOnlyList<JumpListItemSpec> Items);

/// <summary>Localized display strings used when populating the taskbar Jump List.</summary>
public sealed record JumpListLabels(
    string RecentFoldersTitle,
    string RecentFilesTitle,
    string NewWindowTitle,
    string NewWindowDescription);

/// <summary>Snapshot of paths, icons, and labels needed to synchronize the taskbar Jump List.</summary>
public sealed record JumpListSyncInput(
    string ExecutablePath,
    JumpListIcon FileIcon,
    JumpListIcon AppIcon,
    JumpListIcon? FolderIcon,
    IReadOnlyList<string> RecentFolders,
    IReadOnlyList<string> RecentFiles,
    JumpListLabels Labels,
    int MaxFolders = 5,
    int MaxFiles = 10);

/// <summary>Outcome of a Jump List sync, including items the user removed from the taskbar menu.</summary>
public sealed record JumpListSyncResult(
    bool Succeeded,
    IReadOnlyList<string> RemovedFiles,
    IReadOnlyList<string> RemovedFolders,
    int? ErrorCode = null);

/// <summary>Represents one transactional Jump List update session.</summary>
public interface IJumpListSession : IDisposable
{
    uint MinSlots { get; }

    IReadOnlyList<string> RemovedArguments { get; }

    bool TryAppendCategory(JumpListCategorySpec category, out int hresult);

    bool TryAddUserTasks(IReadOnlyList<JumpListItemSpec> tasks, out int hresult);

    bool TryCommit(out int hresult);

    void Abort();
}

/// <summary>Abstracts Windows taskbar Jump List COM APIs so sync logic is testable without shell state.</summary>
public interface IJumpListBackend
{
    JumpListIcon ResolveFolderIcon();

    IJumpListSession? TryBeginSession(out int hresult);
}
