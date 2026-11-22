using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.Markdown.Inlines;
using Quill.Editor.Tests.Conformance;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Inlines;

/// <summary>
/// The acceptance criterion of the inline parser adapter (06-markdown-io.md): every leaf source is cut
/// into nodes that tile it without gaps, over the specification corpora and random sources.
/// </summary>
public sealed class InlineTilingTests
{
    private const string Alphabet = "ab *_~^=$`[]()!<>\\:\n &;#^|/.\"'中，";

    [Theory]
    [InlineData("cm")]
    [InlineData("gfm")]
    public void SpecExampleLeavesTile(string corpus)
    {
        var examples = corpus == "cm" ? SpecCorpus.CommonMark : SpecCorpus.Gfm;
        int leaves = 0;
        foreach (var example in examples)
        {
            var document = MarkdownImporter.Import(example.Markdown, new SequentialBlockIdSource()).Document;
            var references = ReferenceIndex.Of(document);
            foreach (var leaf in Navigation.Leaves(document).OfType<TextLeafBlock>())
            {
                var tree = MarkdownInlines.Parser.Parse(leaf.Text, leaf.InlineSyntax, references.IsLinkDefined);
                InlineDump.AssertTiles(tree);
                leaves++;
            }
        }

        Assert.True(leaves > 500);
        Assert.Equal(0, InlineTreeBuilder.DroppedNodes);
    }

    [Theory]
    [InlineData(InlineSyntaxSet.Full)]
    [InlineData(InlineSyntaxSet.FootnoteContent)]
    public void RandomSourcesTile(InlineSyntaxSet syntax)
    {
        var random = new Random(20261029);
        for (int i = 0; i < 20000; i++)
        {
            int length = random.Next(1, 24);
            var chars = new char[length];
            for (int c = 0; c < length; c++)
            {
                chars[c] = Alphabet[random.Next(Alphabet.Length)];
            }

            string source = new(chars);
            var tree = MarkdownInlines.Parser.Parse(source, syntax, static label => label.Length % 2 == 0);
            InlineDump.AssertTiles(tree);
        }

        Assert.Equal(0, InlineTreeBuilder.DroppedNodes);
    }

    [Fact]
    public void ReflinksResolveAgainstTheDocumentsDefinitions()
    {
        var document = MarkdownImporter.Import("[Foo]\n\n[foo]: /url\n", new SequentialBlockIdSource()).Document;
        var paragraph = Navigation.Leaves(document).OfType<Paragraph>().First();
        var tree = MarkdownInlines.Parser.Parse(paragraph.Text, paragraph.InlineSyntax, ReferenceIndex.Of(document).IsLinkDefined);
        Assert.Equal(InlineKind.Link, Assert.Single(tree.Nodes).Kind);
    }
}
