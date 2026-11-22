using System.Text.Json;

namespace Quill.Editor.Files;

/// <summary>
/// The recently opened files (M6.4): at most <see cref="Capacity"/> full paths, most recent first,
/// compared without case (Windows paths), kept in a JSON file. A file that cannot be read or written is
/// treated as an empty list; the list is a convenience and never stops the app.
/// </summary>
public sealed class RecentFileList
{
    public const int DefaultCapacity = 10;

    private readonly string _storage;
    private readonly List<string> _paths = [];

    public RecentFileList(string storage, int capacity = DefaultCapacity)
    {
        _storage = storage;
        Capacity = capacity;
        Load();
    }

    /// <summary>Raised after the list changed.</summary>
    public event EventHandler? Changed;

    public int Capacity { get; }

    /// <summary>The paths, most recent first.</summary>
    public IReadOnlyList<string> Paths => _paths;

    /// <summary>The default storage: <c>%LOCALAPPDATA%\Quill\recent.json</c>.</summary>
    public static string DefaultStorage =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Quill", "recent.json");

    /// <summary>Puts a file at the front (opened or saved under a new name).</summary>
    public void Add(string path)
    {
        string full = Path.GetFullPath(path);
        _paths.RemoveAll(existing => string.Equals(existing, full, StringComparison.OrdinalIgnoreCase));
        _paths.Insert(0, full);
        if (_paths.Count > Capacity)
        {
            _paths.RemoveRange(Capacity, _paths.Count - Capacity);
        }

        Save();
    }

    /// <summary>Takes a file off the list (it no longer exists).</summary>
    public void Remove(string path)
    {
        if (_paths.RemoveAll(existing => string.Equals(existing, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) > 0)
        {
            Save();
        }
    }

    public void Clear()
    {
        if (_paths.Count > 0)
        {
            _paths.Clear();
            Save();
        }
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_storage) && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_storage)) is { } paths)
            {
                _paths.AddRange(paths.Where(static path => !string.IsNullOrWhiteSpace(path)).Take(Capacity));
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            _paths.Clear();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storage)!);
            File.WriteAllText(_storage, JsonSerializer.Serialize(_paths));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The list stays in memory for this session.
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
