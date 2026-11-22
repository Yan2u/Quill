using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Conformance;

/// <summary>
/// Tests the syntax generation path over the specification corpora: import an example, strip all
/// syntax metadata, serialize (so every marker, prefix and separator is generated), import again and
/// compare the structure (06-markdown-io.md: <c>Import(Serialize(d))</c> is structurally equivalent).
/// </summary>
public sealed class SpecRegenerationTests
{
    /// <summary>
    /// Examples whose content can only be expressed with their original syntax. CommonMark numbering;
    /// the GFM corpus holds the same examples under other numbers.
    /// </summary>
    private static readonly Dictionary<int, string> CommonMarkLimitations = new()
    {
        // "===" is a lazy paragraph line; with a generated "> " prefix it becomes a Setext underline.
        [93] = "lazy continuation line that reads as an underline when prefixed",

        // A paragraph after the list keeps two leading spaces in its text (leaf sources keep leading
        // whitespace); after "- foo" those spaces reach the item's content column.
        [276] = "paragraph with leading spaces after a regenerated list",

        // "-" alone is text (Typora), a lazy line of the item's paragraph; indented to the item's
        // content it becomes a Setext underline.
        [281] = "lazy continuation line that reads as an underline when prefixed",
    };

    private static readonly Dictionary<int, string> GfmLimitations = new()
    {
        [63] = "lazy continuation line that reads as an underline when prefixed",
        [254] = "paragraph with leading spaces after a regenerated list",
        [259] = "lazy continuation line that reads as an underline when prefixed",
    };

    public static TheoryData<int> CommonMarkNumbers =>
        [.. SpecCorpus.CommonMark.Select(static example => example.Number).Where(static number => !CommonMarkLimitations.ContainsKey(number))];

    public static TheoryData<int> GfmNumbers =>
        [.. SpecCorpus.Gfm.Select(static example => example.Number).Where(static number => !GfmLimitations.ContainsKey(number))];

    [Theory]
    [MemberData(nameof(CommonMarkNumbers))]
    public void CommonMarkExampleRegenerates(int number)
    {
        var (expected, actual) = Regenerate(SpecCorpus.CommonMark[number - 1]);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [MemberData(nameof(GfmNumbers))]
    public void GfmExampleRegenerates(int number)
    {
        var (expected, actual) = Regenerate(SpecCorpus.Gfm[number - 1]);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void KnownLimitationsStillDiffer()
    {
        foreach (var number in CommonMarkLimitations.Keys)
        {
            var (expected, actual) = Regenerate(SpecCorpus.CommonMark[number - 1]);
            Assert.NotEqual(expected, actual);
        }

        foreach (var number in GfmLimitations.Keys)
        {
            var (expected, actual) = Regenerate(SpecCorpus.Gfm[number - 1]);
            Assert.NotEqual(expected, actual);
        }
    }

    private static (string Expected, string Actual) Regenerate(SpecExample example)
    {
        var document = MarkdownImporter.Import(example.Markdown, new SequentialBlockIdSource()).Document;
        string generated = MarkdownSerializer.Serialize(SyntaxStripper.Strip(document));
        var reimported = MarkdownImporter.Import(generated, new SequentialBlockIdSource()).Document;
        return (ModelDump.Structure(document), ModelDump.Structure(reimported));
    }
}
