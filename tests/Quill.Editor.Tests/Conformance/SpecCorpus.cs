using System.Text.Json;
using System.Text.RegularExpressions;

namespace Quill.Editor.Tests.Conformance;

/// <summary>
/// One example of a Markdown specification corpus.
/// </summary>
public sealed record SpecExample(int Number, string Section, string Markdown, string Extension);

/// <summary>
/// Loads the specification examples shipped in the repository's testdata folder (testdata/README.md).
/// </summary>
public static partial class SpecCorpus
{
    private static readonly Lazy<IReadOnlyList<SpecExample>> CommonMarkExamples = new(LoadCommonMark);
    private static readonly Lazy<IReadOnlyList<SpecExample>> GfmExamples = new(LoadGfm);

    /// <summary>CommonMark 0.31.2, 652 examples.</summary>
    public static IReadOnlyList<SpecExample> CommonMark => CommonMarkExamples.Value;

    /// <summary>GFM 0.29-gfm as kept in cmark-gfm, 672 examples.</summary>
    public static IReadOnlyList<SpecExample> Gfm => GfmExamples.Value;

    private static string TestDataPath(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "testdata", .. parts]);

    private static IReadOnlyList<SpecExample> LoadCommonMark()
    {
        using var stream = File.OpenRead(TestDataPath("commonmark", "spec.json"));
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray()
            .Select(static element => new SpecExample(
                element.GetProperty("example").GetInt32(),
                element.GetProperty("section").GetString()!,
                element.GetProperty("markdown").GetString()!,
                string.Empty))
            .ToList();
    }

    private static IReadOnlyList<SpecExample> LoadGfm()
    {
        var fence = new string('`', 32);
        var lines = File.ReadAllText(TestDataPath("gfm", "spec.txt")).Split('\n');
        var examples = new List<SpecExample>();
        string section = string.Empty;
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var heading = HeadingPattern().Match(line);
            if (heading.Success)
            {
                section = heading.Groups[1].Value;
                continue;
            }

            if (!line.StartsWith(fence + " example", StringComparison.Ordinal))
            {
                continue;
            }

            string extension = line[(fence.Length + " example".Length)..].Trim();
            var markdown = new List<string>();
            for (i++; lines[i] != "."; i++)
            {
                markdown.Add(lines[i]);
            }

            while (lines[i] != fence)
            {
                i++;
            }

            string text = markdown.Count == 0 ? string.Empty : string.Join('\n', markdown) + "\n";
            examples.Add(new SpecExample(examples.Count + 1, section, text.Replace('→', '\t'), extension));
        }

        return examples;
    }

    [GeneratedRegex(@"^#{1,6} +(.+)$")]
    private static partial Regex HeadingPattern();
}
