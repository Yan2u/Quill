using Quill.Editor.Documents;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Conformance;

/// <summary>
/// Examples whose structure in Quill intentionally differs from CommonMark to match Typora. They still
/// round-trip byte for byte; a test comparing rendered HTML with the specification must skip them.
/// </summary>
public sealed class TyporaDeviationTests
{
    /// <summary>
    /// Multi-line Setext headings: Typora has no heading that spans lines, so only the last line before
    /// the underline is the heading and the lines above it stay a paragraph.
    /// </summary>
    private const string MultiLineSetext = "multi-line Setext heading";

    /// <summary>
    /// A list marker alone on its line: Typora needs a space or tab after a marker, so the line is text
    /// where CommonMark has an empty list item.
    /// </summary>
    private const string MarkerAlone = "list marker alone on its line";

    public static readonly IReadOnlyDictionary<int, string> CommonMark = new Dictionary<int, string>
    {
        [81] = MultiLineSetext,
        [82] = MultiLineSetext,
        [95] = MultiLineSetext,
        [278] = MarkerAlone,
        [280] = MarkerAlone,
        [281] = MarkerAlone,
        [283] = MarkerAlone,
        [284] = MarkerAlone,
        [315] = MarkerAlone,
    };

    public static readonly IReadOnlyDictionary<int, string> Gfm = new Dictionary<int, string>
    {
        [51] = MultiLineSetext,
        [52] = MultiLineSetext,
        [65] = MultiLineSetext,
        [256] = MarkerAlone,
        [258] = MarkerAlone,
        [259] = MarkerAlone,
        [261] = MarkerAlone,
        [262] = MarkerAlone,
        [295] = MarkerAlone,
    };

    public static TheoryData<string, int> MultiLineSetextExamples => Examples(MultiLineSetext);

    public static TheoryData<string, int> MarkerAloneExamples => Examples(MarkerAlone);

    [Theory]
    [MemberData(nameof(MultiLineSetextExamples))]
    public void MultiLineSetextHeadingKeepsOnlyItsLastLine(string corpus, int number)
    {
        var document = RoundTrip(Example(corpus, number).Markdown);
        Assert.Equal([BlockKind.Paragraph, BlockKind.Heading], document.Children.Select(static block => block.Kind));
        Assert.DoesNotContain('\n', ((Heading)document.Children[1]).Text);
    }

    [Fact]
    public void ParagraphAboveTheHeadingStaysAdjacent()
    {
        var document = RoundTrip("Foo\n  Bar  \n---\n");
        Assert.Equal("Foo", ((Paragraph)document.Children[0]).Text);
        var heading = (Heading)document.Children[1];
        Assert.Equal((2, "Bar"), (heading.Level, heading.Text));
        Assert.True(heading.SeparatorBefore!.IsAdjacent);
    }

    /// <summary>Each of these examples has an empty list item in CommonMark; in Quill no item is empty.</summary>
    [Theory]
    [MemberData(nameof(MarkerAloneExamples))]
    public void MarkerAloneIsNotAnEmptyItem(string corpus, int number)
    {
        var document = RoundTrip(Example(corpus, number).Markdown);
        Assert.All(Items(document), static item =>
        {
            Assert.False(item.Syntax!.OpensWithBlankLine);
            Assert.False(item.Children[0] is Paragraph { IsEmpty: true });
        });
    }

    private static TheoryData<string, int> Examples(string reason) =>
    [
        .. CommonMark.Where(pair => pair.Value == reason).Select(static pair => ("cm", pair.Key)),
        .. Gfm.Where(pair => pair.Value == reason).Select(static pair => ("gfm", pair.Key)),
    ];

    private static SpecExample Example(string corpus, int number) =>
        (corpus == "cm" ? SpecCorpus.CommonMark : SpecCorpus.Gfm)[number - 1];

    private static IEnumerable<ListItem> Items(Block block)
    {
        if (block is ListItem item)
        {
            yield return item;
        }

        if (block is ContainerBlock container)
        {
            foreach (var child in container.Children.SelectMany(Items))
            {
                yield return child;
            }
        }
    }
}
