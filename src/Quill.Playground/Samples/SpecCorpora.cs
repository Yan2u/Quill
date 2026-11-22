using System.Text.Json;

namespace Quill.Playground.Samples;

/// <summary>One example of a specification corpus.</summary>
internal sealed record SpecExample(int Number, string Section, string Markdown);

/// <summary>
/// The CommonMark and GFM specification examples from the repository's <c>testdata</c> folder
/// (the same corpora as tests/Quill.Editor.Tests/Conformance/SpecCorpus.cs), for rendering checks.
/// </summary>
internal static class SpecCorpora
{
    private const string Fence = "````````````````````````````````";

    /// <summary>CommonMark 0.31.2, 652 examples; empty when the repository is not found.</summary>
    public static IReadOnlyList<SpecExample> CommonMark()
    {
        if (RepositoryPaths.TestData("commonmark", "spec.json") is not { } path || !File.Exists(path))
        {
            return [];
        }

        using var stream = File.OpenRead(path);
        using var json = JsonDocument.Parse(stream);
        return json.RootElement.EnumerateArray()
            .Select(static element => new SpecExample(
                element.GetProperty("example").GetInt32(),
                element.GetProperty("section").GetString()!,
                element.GetProperty("markdown").GetString()!))
            .ToList();
    }

    /// <summary>GFM 0.29-gfm, 672 examples; empty when the repository is not found.</summary>
    public static IReadOnlyList<SpecExample> Gfm()
    {
        if (GfmSpecPath() is not { } path)
        {
            return [];
        }

        var lines = File.ReadAllText(path).Split('\n');
        var examples = new List<SpecExample>();
        string section = string.Empty;
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith('#'))
            {
                section = line.TrimStart('#').Trim();
                continue;
            }

            if (!line.StartsWith(Fence + " example", StringComparison.Ordinal))
            {
                continue;
            }

            var markdown = new List<string>();
            for (i++; i < lines.Length && lines[i] != "."; i++)
            {
                markdown.Add(lines[i]);
            }

            while (i < lines.Length && lines[i] != Fence)
            {
                i++;
            }

            string text = markdown.Count == 0 ? string.Empty : string.Join('\n', markdown) + "\n";
            examples.Add(new SpecExample(examples.Count + 1, section, text.Replace('→', '\t')));
        }

        return examples;
    }

    /// <summary>The GFM specification itself: a long Markdown document.</summary>
    public static string? GfmSpecPath() =>
        RepositoryPaths.TestData("gfm", "spec.txt") is { } path && File.Exists(path) ? path : null;
}
