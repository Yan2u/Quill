using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Tests.Conformance;

/// <summary>
/// 06-markdown-io.md round-trip invariant for unedited documents: <c>Serialize(Import(m)) == m</c>,
/// byte for byte, over every example of the CommonMark and GFM specifications.
/// </summary>
public sealed class SpecRoundTripTests
{
    public static TheoryData<int> CommonMarkNumbers => [.. SpecCorpus.CommonMark.Select(static example => example.Number)];

    public static TheoryData<int> GfmNumbers => [.. SpecCorpus.Gfm.Select(static example => example.Number)];

    [Fact]
    public void CorporaAreComplete()
    {
        Assert.Equal(652, SpecCorpus.CommonMark.Count);
        Assert.Equal(672, SpecCorpus.Gfm.Count);
    }

    [Theory]
    [MemberData(nameof(CommonMarkNumbers))]
    public void CommonMarkExampleRoundTrips(int number)
    {
        AssertRoundTrip(SpecCorpus.CommonMark[number - 1]);
    }

    [Theory]
    [MemberData(nameof(GfmNumbers))]
    public void GfmExampleRoundTrips(int number)
    {
        AssertRoundTrip(SpecCorpus.Gfm[number - 1]);
    }

    [Theory]
    [InlineData("cm", "\n\n")]
    [InlineData("cm", "\n")]
    [InlineData("gfm", "\n\n")]
    [InlineData("gfm", "\n")]
    public void ConcatenatedCorpusRoundTrips(string corpus, string separator)
    {
        // Joining the examples makes them interact: unclosed fences swallow later examples, lists and
        // quotes continue across examples, definitions move between containers.
        var examples = corpus == "cm" ? SpecCorpus.CommonMark : SpecCorpus.Gfm;
        string markdown = string.Join(separator, examples.Select(static example => example.Markdown));
        AssertRoundTrip(new SpecExample(0, "all", markdown, string.Empty));
    }

    private static void AssertRoundTrip(SpecExample example)
    {
        var result = MarkdownImporter.Import(example.Markdown, new SequentialBlockIdSource());
        Assert.Empty(result.Diagnostics);
        Assert.Empty(DocumentValidator.Validate(result.Document));
        Assert.Equal(example.Markdown, MarkdownSerializer.Serialize(result.Document));
    }
}
