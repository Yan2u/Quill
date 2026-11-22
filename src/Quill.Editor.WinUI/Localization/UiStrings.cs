using System.Collections.Frozen;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Quill.Editor.Input;

namespace Quill.Editor.WinUI.Localization;

/// <summary>
/// Runtime UI string lookup backed by embedded English and Simplified Chinese JSON tables.
/// Calling <see cref="SetLanguage"/> updates <see cref="Language"/> and raises
/// <see cref="LanguageChanged"/> so live views refresh immediately without restarting.
/// </summary>
public static class UiStrings
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo ZhCn = CultureInfo.GetCultureInfo("zh-CN");

    private static readonly FrozenDictionary<string, string> English = Load("Quill.Editor.WinUI.Localization.en.json");
    private static readonly FrozenDictionary<string, string> Chinese = Load("Quill.Editor.WinUI.Localization.zh-CN.json");

    private static volatile FrozenDictionary<string, string> _current = English;
    private static UiLanguage _language = UiLanguage.English;

    public static UiLanguage Language => _language;

    public static CultureInfo Culture => _language == UiLanguage.Chinese ? ZhCn : EnUs;

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(UiLanguage language)
    {
        if (_language == language)
        {
            return;
        }

        _language = language;
        _current = language == UiLanguage.Chinese ? Chinese : English;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) =>
        _current.TryGetValue(key, out string? value) ? value
        : English.TryGetValue(key, out string? fallback) ? fallback
        : key;

    public static string Format(string key, params object[] args) =>
        string.Format(Culture, Get(key), args);

    public static string MenuLabel(MenuEntry entry)
    {
        string key = entry.Id switch
        {
            "select-scope" => entry.Label == "Select cell" ? "menu.select-scope.cell" : "menu.select-scope",
            "select-line" => entry.Label == "Select row" ? "menu.select-line.row" : "menu.select-line",
            "insert-image" => entry.Label == "Image" ? "menu.insert-image.context" : "menu.insert-image",
            "insert-table" => entry.Label == "Table" ? "menu.insert-table.context" : "menu.insert-table",
            "paragraph-above" => entry.Label == "Paragraph above" ? "menu.paragraph-above.context" : "menu.paragraph-above",
            "paragraph-below" => entry.Label == "Paragraph below" ? "menu.paragraph-below.context" : "menu.paragraph-below",
            _ => $"menu.{entry.Id}",
        };
        return _current.TryGetValue(key, out string? value) ? value
            : English.TryGetValue(key, out string? fallback) ? fallback
            : entry.Label;
    }

    internal static IReadOnlyDictionary<string, string> Table(UiLanguage language) =>
        language == UiLanguage.Chinese ? Chinese : English;

    private static FrozenDictionary<string, string> Load(string resourceName)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded localization resource '{resourceName}'.");
        Dictionary<string, string> values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidOperationException($"Failed to parse localization resource '{resourceName}'.");
        return values.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
