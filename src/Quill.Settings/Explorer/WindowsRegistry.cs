using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Quill.Settings.Explorer;

/// <summary>The Windows registry, for keys of HKEY_CURRENT_USER and HKEY_CLASSES_ROOT.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistry : IRegistry
{
    private const int ShcneAssocChanged = 0x08000000;

    public bool KeyExists(string key)
    {
        using var opened = Open(key, writable: false);
        return opened is not null;
    }

    public string? GetValue(string key, string? name)
    {
        using var opened = Open(key, writable: false);
        return opened?.GetValue(name) as string;
    }

    public void SetValue(string key, string? name, string value)
    {
        var (root, path) = Split(key);
        using var created = root.CreateSubKey(path, writable: true);
        created.SetValue(name, value, RegistryValueKind.String);
    }

    public void DeleteValue(string key, string? name)
    {
        using var opened = Open(key, writable: true);
        opened?.DeleteValue(name ?? string.Empty, throwOnMissingValue: false);
    }

    public void DeleteKey(string key)
    {
        var (root, path) = Split(key);
        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
    }

    public void DeleteKeyIfEmpty(string key)
    {
        using (var opened = Open(key, writable: false))
        {
            if (opened is null || opened.ValueCount > 0 || opened.SubKeyCount > 0)
            {
                return;
            }
        }

        var (root, path) = Split(key);
        root.DeleteSubKey(path, throwOnMissingSubKey: false);
    }

    public void NotifyAssociationsChanged() => SHChangeNotify(ShcneAssocChanged, 0, IntPtr.Zero, IntPtr.Zero);

    private static RegistryKey? Open(string key, bool writable)
    {
        var (root, path) = Split(key);
        return root.OpenSubKey(path, writable);
    }

    private static (RegistryKey Root, string Path) Split(string key)
    {
        int separator = key.IndexOf('\\');
        string hive = separator < 0 ? key : key[..separator];
        string path = separator < 0 ? string.Empty : key[(separator + 1)..];
        var root = hive.ToUpperInvariant() switch
        {
            "HKEY_CURRENT_USER" or "HKCU" => Registry.CurrentUser,
            "HKEY_CLASSES_ROOT" or "HKCR" => Registry.ClassesRoot,
            _ => throw new ArgumentException($"Only HKEY_CURRENT_USER and HKEY_CLASSES_ROOT are used: {key}", nameof(key)),
        };
        return (root, path);
    }

    [DllImport("shell32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
}
