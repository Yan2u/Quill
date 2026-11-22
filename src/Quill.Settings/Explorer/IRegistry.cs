namespace Quill.Settings.Explorer;

/// <summary>
/// The few registry operations the Explorer integration needs, on full key paths such as
/// <c>HKEY_CURRENT_USER\Software\Classes\.md</c>. A null value name is the key's default value. Tests use
/// <see cref="MemoryRegistry"/> and never touch the real registry (14-settings.md).
/// </summary>
public interface IRegistry
{
    bool KeyExists(string key);

    /// <summary>A string value, or null when the key or the value does not exist.</summary>
    string? GetValue(string key, string? name);

    /// <summary>Sets a string value, creating the key (and its parents) when needed.</summary>
    void SetValue(string key, string? name, string value);

    void DeleteValue(string key, string? name);

    /// <summary>Deletes a key with all it holds.</summary>
    void DeleteKey(string key);

    /// <summary>Deletes a key that holds no values and no subkeys; nothing happens otherwise.</summary>
    void DeleteKeyIfEmpty(string key);

    /// <summary>Tells Explorer that file associations changed.</summary>
    void NotifyAssociationsChanged();
}
