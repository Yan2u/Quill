using System.Text.Json;

namespace Quill.Editor.Files;

/// <summary>
/// Thread-safe, capacity-bounded recent path list backed by atomic JSON persistence.
/// </summary>
internal sealed class RecentPathList
{
    private readonly Lock _sync = new();
    private readonly string _storage;
    private readonly Func<string?, string?> _normalize;
    private readonly List<string> _paths = [];
    private volatile string[] _snapshot = [];

    public RecentPathList(string storage, int capacity, Func<string?, string?> normalize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        ArgumentNullException.ThrowIfNull(normalize);

        _storage = storage;
        Capacity = capacity;
        _normalize = normalize;
        Load();
    }

    public int Capacity { get; }

    public IReadOnlyList<string> Paths => _snapshot;

    public bool Add(string path)
    {
        if (_normalize(path) is not { } normalized)
        {
            return false;
        }

        lock (_sync)
        {
            _paths.RemoveAll(existing => string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
            _paths.Insert(0, normalized);
            if (_paths.Count > Capacity)
            {
                _paths.RemoveRange(Capacity, _paths.Count - Capacity);
            }

            _snapshot = [.. _paths];
            SaveLocked();
            return true;
        }
    }

    public bool Remove(string path) => RemoveRange([path]);

    public bool RemoveRange(IEnumerable<string> paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        HashSet<string>? targets = null;
        foreach (string candidate in paths)
        {
            if (_normalize(candidate) is { } normalized)
            {
                targets ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                targets.Add(normalized);
            }
        }

        if (targets is null || targets.Count == 0)
        {
            return false;
        }

        lock (_sync)
        {
            if (_paths.RemoveAll(targets.Contains) == 0)
            {
                return false;
            }

            _snapshot = [.. _paths];
            SaveLocked();
            return true;
        }
    }

    public bool Clear()
    {
        lock (_sync)
        {
            if (_paths.Count == 0)
            {
                return false;
            }

            _paths.Clear();
            _snapshot = [];
            SaveLocked();
            return true;
        }
    }

    private void Load()
    {
        lock (_sync)
        {
            try
            {
                if (File.Exists(_storage) && JsonSerializer.Deserialize<List<string>>(File.ReadAllText(_storage)) is { } rawPaths)
                {
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string raw in rawPaths)
                    {
                        if (_normalize(raw) is { } normalized && seen.Add(normalized))
                        {
                            _paths.Add(normalized);
                            if (_paths.Count >= Capacity)
                            {
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
            {
                _paths.Clear();
            }

            _snapshot = [.. _paths];
        }
    }

    private void SaveLocked()
    {
        string? tempPath = null;
        try
        {
            string? directory = Path.GetDirectoryName(_storage);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string fileName = $"{Path.GetFileName(_storage)}.{Guid.NewGuid():N}.tmp";
            tempPath = string.IsNullOrEmpty(directory) ? fileName : Path.Combine(directory, fileName);
            File.WriteAllText(tempPath, JsonSerializer.Serialize(_paths));
            File.Move(tempPath, _storage, overwrite: true);
            tempPath = null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // The list stays in memory for this session.
        }
        finally
        {
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception cleanupError) when (cleanupError is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }
}
