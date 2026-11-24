namespace Quill.Editor.Files;

/// <summary>
/// The recently opened folders: at most <see cref="Capacity"/> normalized full directory paths, most recent
/// first, compared without case (Windows paths), kept in a JSON file. A file that cannot be read or written
/// is treated as an empty list; the list is a convenience and never stops the app.
/// </summary>
public sealed class RecentFolderList
{
    public const int DefaultCapacity = 10;

    private readonly RecentPathList _inner;

    public RecentFolderList(string storage, int capacity = DefaultCapacity)
    {
        _inner = new RecentPathList(storage, capacity, TryNormalizeFolderPath);
    }

    /// <summary>Raised after the list changed.</summary>
    public event EventHandler? Changed;

    public int Capacity => _inner.Capacity;

    /// <summary>Immutable snapshot of the folder paths, most recent first.</summary>
    public IReadOnlyList<string> Paths => _inner.Paths;

    /// <summary>The default storage: <c>%LOCALAPPDATA%\Quill\recent-folders.json</c>.</summary>
    public static string DefaultStorage =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill", "recent-folders.json");

    /// <summary>Puts a folder at the front (opened from the sidebar, File menu, startup, or drop).</summary>
    public void Add(string path)
    {
        if (_inner.Add(path))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Takes a folder off the list (it no longer exists or was removed).</summary>
    public void Remove(string path)
    {
        if (_inner.Remove(path))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Removes all matching folders in a single pass and returns whether any entry was removed.</summary>
    public bool RemoveRange(IEnumerable<string> paths)
    {
        if (!_inner.RemoveRange(paths))
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Clear()
    {
        if (_inner.Clear())
        {
            Changed?.Invoke(this, EventArgs.Empty);
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
            string full = Path.GetFullPath(path.Trim());
            return Path.TrimEndingDirectorySeparator(full);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}
