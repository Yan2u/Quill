using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quill.Settings.Themes;

/// <summary>
/// User themes in <c>%LOCALAPPDATA%\Quill\themes</c>, one JSON file each, <c>{id}.json</c> (15-themes.md, Q112).
/// A file that cannot be read is skipped; unknown keys are ignored; writing goes to a temporary file that then
/// replaces the old one. Built-in themes are not stored here.
/// </summary>
public sealed class ThemeStore
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public ThemeStore(string folder)
    {
        Folder = folder;
    }

    public static string DefaultFolder => Path.Combine(AppDataFolder.Path, "themes");

    public string Folder { get; }

    /// <summary>The themes in the folder, by name; files that cannot be read are left out.</summary>
    public IReadOnlyList<ThemeFile> LoadAll()
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var themes = new List<ThemeFile>();
        foreach (string path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            if (Read(path) is { } theme)
            {
                themes.Add(theme);
            }
        }

        return [.. themes.OrderBy(static theme => theme.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Writes a theme to <c>{id}.json</c>.</summary>
    public void Save(ThemeFile theme)
    {
        if (!IsValidId(theme.Id))
        {
            throw new ArgumentException($"Not a theme id: {theme.Id}", nameof(theme));
        }

        var values = new JsonObject();
        foreach (var (key, value) in theme.Values.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            values[key] = value;
        }

        var json = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["id"] = theme.Id,
            ["name"] = theme.Name,
            ["description"] = theme.Description,
            ["created"] = theme.Created.ToString("O"),
            ["modified"] = theme.Modified.ToString("O"),
            ["basedOn"] = theme.BasedOn,
            ["values"] = values,
        };
        Directory.CreateDirectory(Folder);
        string path = PathOf(theme.Id);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, json.ToJsonString(WriteOptions));
        File.Move(temporary, path, overwrite: true);
    }

    public void Delete(string id)
    {
        if (IsValidId(id) && File.Exists(PathOf(id)))
        {
            File.Delete(PathOf(id));
        }
    }

    /// <summary>A new id: letters and digits only, so it is a safe file name.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Ids name files: letters, digits, '-' and '_' only.</summary>
    public static bool IsValidId(string id) => id.Length is > 0 and <= 64 && id.All(static c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    /// <summary>
    /// The name of a copy (15-themes.md): "{name} copy", then "{name} copy 2", "{name} copy 3"… whichever is
    /// not taken (names compare without case).
    /// </summary>
    public static string CopyName(string name, IEnumerable<string> taken)
    {
        var names = new HashSet<string>(taken, StringComparer.CurrentCultureIgnoreCase);
        string candidate = $"{name} copy";
        for (int number = 2; names.Contains(candidate); number++)
        {
            candidate = $"{name} copy {number}";
        }

        return candidate;
    }

    private string PathOf(string id) => Path.Combine(Folder, id + ".json");

    private static ThemeFile? Read(string path)
    {
        try
        {
            if (JsonNode.Parse(File.ReadAllText(path)) is not JsonObject json
                || JsonFields.String(json, "id", null) is not { } id || !IsValidId(id)
                || !string.Equals(id, Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase)
                || JsonFields.String(json, "basedOn", null) is not { Length: > 0 } basedOn)
            {
                return null;
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (json["values"] is JsonObject entries)
            {
                foreach (var (key, node) in entries)
                {
                    if (node is JsonValue value && value.TryGetValue(out string? text))
                    {
                        values[key] = text;
                    }
                }
            }

            return new ThemeFile
            {
                Id = id,
                Name = JsonFields.String(json, "name", null) is { Length: > 0 } name ? name : id,
                Description = JsonFields.String(json, "description", null) ?? string.Empty,
                Created = Date(json, "created"),
                Modified = Date(json, "modified"),
                BasedOn = basedOn,
                Values = values,
            };
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static DateTimeOffset Date(JsonObject json, string key) =>
        DateTimeOffset.TryParse(JsonFields.String(json, key, null), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var date)
            ? date
            : DateTimeOffset.MinValue;
}
