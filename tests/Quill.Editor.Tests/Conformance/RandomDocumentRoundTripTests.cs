using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Conformance;

/// <summary>
/// Import must accept any text and reproduce it exactly. Random documents are assembled from the lines
/// of both specification corpora plus constructs Quill adds (footnotes, [toc], task items, tabs, the
/// zero-width space), with a fixed seed per test case.
/// </summary>
public sealed class RandomDocumentRoundTripTests
{
    private const int DocumentsPerSeed = 400;

    private static readonly string[] Extras =
    [
        "[^1]: note", "[^x]:", "    [^2]: indented", "[toc]", "- [ ] task", "- [x] done", "1. one", "10) ten",
        "> ", ">", "\t", "  \t- x", "​    zero width", "| a | b |", "|---|:-:|", "$$", "---", "***", "<div>",
        "</div>", "```", "~~~ x", "[a]: <b c> 'd'", "* * *", "#", "######## x",
    ];

    private static readonly Lazy<string[]> CorpusLines = new(static () =>
        SpecCorpus.CommonMark.Concat(SpecCorpus.Gfm).SelectMany(static example => example.Markdown.Split('\n')).ToArray());

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 10)];

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomDocumentsRoundTrip(int seed)
    {
        var random = new Random(seed);
        var lines = CorpusLines.Value;
        for (int i = 0; i < DocumentsPerSeed; i++)
        {
            var picked = Enumerable.Range(0, random.Next(1, 25))
                .Select(_ => random.Next(5) == 0 ? Extras[random.Next(Extras.Length)] : lines[random.Next(lines.Length)]);
            string lineEnding = random.Next(4) == 0 ? "\r\n" : "\n";
            string markdown = string.Join(lineEnding, picked) + (random.Next(2) == 0 ? lineEnding : string.Empty);

            var result = MarkdownImporter.Import(markdown, new SequentialBlockIdSource());
            Assert.Empty(DocumentValidator.Validate(result.Document));
            string written = MarkdownSerializer.Serialize(result.Document);
            Assert.True(written == markdown, $"Document {i} does not round-trip.\n--- input ---\n{markdown}\n--- output ---\n{written}");
        }
    }

    /// <summary>Inputs that once broke the round trip, found by fuzzing.</summary>
    [Theory]
    [InlineData("- b\n   - d\n    - e")]
    [InlineData("[^1]: note\n  - item\n")]
    [InlineData("-\n  foo\n-\n  ```\n  bar\n  ```\n-\n      baz\n\n\n-   \n  foo\n")]
    [InlineData("   ```\n   aaa\n    aaa\n  aaa\n   ```\n")]
    public void FoundByFuzzing(string markdown)
    {
        RoundTrip(markdown);
    }
}
