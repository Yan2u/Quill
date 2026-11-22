using Microsoft.UI.Xaml.Controls;

namespace Quill.Shell.Settings;

/// <summary>
/// One setting on the page: its section, the name and description that search matches, and the card
/// that shows it. A setting whose feature comes in a later milestone is shown but not available.
/// </summary>
internal sealed class SettingEntry(SettingsSection section, string name, string description, Control card, bool isAvailable)
{
    public SettingsSection Section { get; } = section;

    public string Name { get; set; } = name;

    public string Description { get; set; } = description;

    public Control Card { get; } = card;

    public bool IsAvailable { get; } = isAvailable;

    /// <summary>Whether search for <paramref name="query"/> shows the setting: its name or description contains it.</summary>
    public bool Matches(string query) => query.Length == 0
        || Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);
}
