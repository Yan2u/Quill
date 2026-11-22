namespace Quill.Settings.Themes;

/// <summary>
/// A user theme as stored (15-themes.md, Q112): its metadata, the built-in theme it is based on, and the style
/// slots that differ from it, each as a string (colors <c>#RRGGBB</c> or <c>#AARRGGBB</c>, numbers, paddings
/// <c>left,top,right,bottom</c>, booleans, font names). What a value means is the theming layer's business.
/// </summary>
public sealed record ThemeFile
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = string.Empty;

    public DateTimeOffset Created { get; init; }

    public DateTimeOffset Modified { get; init; }

    /// <summary>The id of the built-in theme the values apply to.</summary>
    public required string BasedOn { get; init; }

    /// <summary>The slots that differ from <see cref="BasedOn"/>, by slot name.</summary>
    public IReadOnlyDictionary<string, string> Values { get; init; } = new Dictionary<string, string>();

    public bool Equals(ThemeFile? other) =>
        other is not null && Id == other.Id && Name == other.Name && Description == other.Description && Created == other.Created
        && Modified == other.Modified && BasedOn == other.BasedOn
        && Values.Count == other.Values.Count && Values.All(pair => other.Values.TryGetValue(pair.Key, out var value) && value == pair.Value);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Modified);
}
