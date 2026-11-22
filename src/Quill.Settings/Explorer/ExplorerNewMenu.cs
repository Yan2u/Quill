namespace Quill.Settings.Explorer;

/// <summary>
/// "New Markdown document" in File Explorer's New menu (14-settings.md). Only <c>HKCU\Software\Classes</c> is
/// written: a <c>ShellNew</c> key with an empty file under <c>.md</c>, marked as Quill's. Explorer shows the
/// entry only for an extension with a file type, so when <c>.md</c> has none, Quill adds one ("Markdown
/// Document") and marks it too. Removing deletes only what carries Quill's marks; existing file
/// associations are never changed.
/// </summary>
public sealed class ExplorerNewMenu(IRegistry registry)
{
    public const string Extension = ".md";
    public const string ProgId = "Quill.md";
    public const string TypeName = "Markdown Document";

    private const string ClassesRoot = @"HKEY_CLASSES_ROOT\" + Extension;
    private const string UserClasses = @"HKEY_CURRENT_USER\Software\Classes";
    private const string UserExtension = UserClasses + @"\" + Extension;
    private const string UserShellNew = UserExtension + @"\ShellNew";
    private const string Mark = "QuillNewMenu";
    private const string ProgIdMark = "QuillProgId";

    public NewMenuState State
    {
        get
        {
            if (registry.GetValue(UserShellNew, Mark) is not null)
            {
                return NewMenuState.AddedByQuill;
            }

            string? type = FileType();
            return registry.KeyExists(UserShellNew) || registry.KeyExists(ClassesRoot + @"\ShellNew")
                || (type is not null && registry.KeyExists($@"{ClassesRoot}\{type}\ShellNew"))
                ? NewMenuState.AddedElsewhere
                : NewMenuState.NotAdded;
        }
    }

    /// <summary>Adds the entry; nothing happens when the menu already has one.</summary>
    public void Add()
    {
        if (State != NewMenuState.NotAdded)
        {
            return;
        }

        if (FileType() is null)
        {
            registry.SetValue(UserExtension, null, ProgId);
            registry.SetValue(UserExtension, ProgIdMark, ProgId);
            registry.SetValue($@"{UserClasses}\{ProgId}", null, TypeName);
        }

        registry.SetValue(UserShellNew, "NullFile", string.Empty);
        registry.SetValue(UserShellNew, Mark, "1");
        registry.NotifyAssociationsChanged();
    }

    /// <summary>Removes what Quill added, and nothing else.</summary>
    public void Remove()
    {
        if (State != NewMenuState.AddedByQuill)
        {
            return;
        }

        registry.DeleteKey(UserShellNew);
        if (registry.GetValue(UserExtension, ProgIdMark) is { Length: > 0 } added)
        {
            if (string.Equals(registry.GetValue(UserExtension, null), added, StringComparison.OrdinalIgnoreCase))
            {
                registry.DeleteValue(UserExtension, null);
            }

            registry.DeleteValue(UserExtension, ProgIdMark);
            registry.DeleteKey($@"{UserClasses}\{added}");
        }

        registry.DeleteKeyIfEmpty(UserExtension);
        registry.NotifyAssociationsChanged();
    }

    /// <summary>The file type (ProgID) of <c>.md</c>, the user's or the machine's; null when there is none.</summary>
    private string? FileType() =>
        (registry.GetValue(UserExtension, null) ?? registry.GetValue(ClassesRoot, null)) is { Length: > 0 } type ? type : null;
}
