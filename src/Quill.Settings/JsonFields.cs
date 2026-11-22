using System.Text.Json;
using System.Text.Json.Nodes;

namespace Quill.Settings;

/// <summary>
/// Reads and writes single values of a JSON object: a value of the wrong type (or a name no enum member
/// has) reads as the fallback. Enums are written as camelCase names.
/// </summary>
internal static class JsonFields
{
    public static bool Bool(JsonObject json, string key, bool fallback) =>
        json[key] is JsonValue value && value.TryGetValue(out bool result) ? result : fallback;

    public static int Int(JsonObject json, string key, int fallback) =>
        json[key] is JsonValue value && value.TryGetValue(out int result) ? result : fallback;

    public static int? NullableInt(JsonObject json, string key, int? fallback) => json.TryGetPropertyValue(key, out var node)
        ? node is null ? null : node is JsonValue value && value.TryGetValue(out int result) ? result : fallback
        : fallback;

    public static double Double(JsonObject json, string key, double fallback) =>
        json[key] is JsonValue value && value.TryGetValue(out double result) && double.IsFinite(result) ? result : fallback;

    public static string? String(JsonObject json, string key, string? fallback) => json.TryGetPropertyValue(key, out var node)
        ? node is null ? null : node is JsonValue value && value.TryGetValue(out string? result) ? result : fallback
        : fallback;

    public static TEnum Enum<TEnum>(JsonObject json, string key, TEnum fallback)
        where TEnum : struct, System.Enum =>
        json[key] is JsonValue value && value.TryGetValue(out string? name)
            && name is { Length: > 0 } && !char.IsAsciiDigit(name[0]) && name[0] != '-'
            && System.Enum.TryParse(name, ignoreCase: true, out TEnum result) && System.Enum.IsDefined(result)
            ? result
            : fallback;

    public static void Set<TEnum>(JsonObject json, string key, TEnum value)
        where TEnum : struct, System.Enum => json[key] = JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
