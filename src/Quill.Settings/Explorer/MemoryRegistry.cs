namespace Quill.Settings.Explorer;

/// <summary>A registry in memory, for tests and previews: keys by full path, compared without case.</summary>
public sealed class MemoryRegistry : IRegistry
{
    private readonly Dictionary<string, Dictionary<string, string>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times Explorer was told about a change.</summary>
    public int Notifications { get; private set; }

    /// <summary>The keys, for tests.</summary>
    public IEnumerable<string> Keys => _keys.Keys;

    public bool KeyExists(string key) => _keys.ContainsKey(key);

    public string? GetValue(string key, string? name) =>
        _keys.TryGetValue(key, out var values) && values.TryGetValue(name ?? string.Empty, out var value) ? value : null;

    public void SetValue(string key, string? name, string value)
    {
        for (string? parent = key; !string.IsNullOrEmpty(parent); parent = Parent(parent))
        {
            if (!_keys.ContainsKey(parent))
            {
                _keys[parent] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        _keys[key][name ?? string.Empty] = value;
    }

    public void DeleteValue(string key, string? name)
    {
        if (_keys.TryGetValue(key, out var values))
        {
            values.Remove(name ?? string.Empty);
        }
    }

    public void DeleteKey(string key)
    {
        foreach (string existing in _keys.Keys.ToList())
        {
            if (string.Equals(existing, key, StringComparison.OrdinalIgnoreCase) || existing.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase))
            {
                _keys.Remove(existing);
            }
        }
    }

    public void DeleteKeyIfEmpty(string key)
    {
        if (_keys.TryGetValue(key, out var values) && values.Count == 0
            && !_keys.Keys.Any(existing => existing.StartsWith(key + "\\", StringComparison.OrdinalIgnoreCase)))
        {
            _keys.Remove(key);
        }
    }

    public void NotifyAssociationsChanged() => Notifications++;

    private static string? Parent(string key) => key.LastIndexOf('\\') is > 0 and var at ? key[..at] : null;
}
