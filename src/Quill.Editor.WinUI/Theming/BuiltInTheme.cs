namespace Quill.Editor.WinUI.Theming;

/// <summary>A theme that comes with the app (15-themes.md): read-only, it cannot be deleted.</summary>
public sealed record BuiltInTheme(string Id, string Name, string Description, MdStyleSheet Sheet)
{
    /// <summary>The description localized for the active interface language.</summary>
    public string LocalizedDescription => Localization.UiStrings.Get($"theme.builtin.{Id}.description");
}
