using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Markdown;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Inlines;

/// <summary>
/// The inline parser adapter (06-markdown-io.md, Q2): node kinds, syntax ranges and gapless tiling.
/// </summary>
public sealed class InlineParserTests
{
    private static readonly IInlineParser Parser = MarkdownInlines.Parser;

    [Theory]
    [InlineData("a **b** c", "\"a \" Strong(\"**\"|\"b\"|\"**\") \" c\"")]
    [InlineData("*a* _b_ __c__", "Emphasis(\"*\"|\"a\"|\"*\") \" \" Emphasis(\"_\"|\"b\"|\"_\") \" \" Strong(\"__\"|\"c\"|\"__\")")]
    [InlineData("***a***", "Emphasis(\"*\"|Strong(\"**\"|\"a\"|\"**\")|\"*\")")]
    [InlineData("**a *b* c**", "Strong(\"**\"|\"a \" Emphasis(\"*\"|\"b\"|\"*\") \" c\"|\"**\")")]
    [InlineData("~~d~~ ~s~ ^p^ ==h==", "Strikethrough(\"~~\"|\"d\"|\"~~\") \" \" Subscript(\"~\"|\"s\"|\"~\") \" \" Superscript(\"^\"|\"p\"|\"^\") \" \" Highlight(\"==\"|\"h\"|\"==\")")]
    [InlineData("``a ` b``", "Code(\"``\"|\"a ` b\"|\"``\")")]
    [InlineData("$m$ $$n$$", "Math(\"$\"|\"m\"|\"$\") \" \" Math(\"$$\"|\"n\"|\"$$\")")]
    [InlineData("x\\*y\\", "\"x\" Escape(\"\\\"|\"*\") \"y\\\"")]
    [InlineData("&copy; :smile:", "Entity(\"&copy;\") \" \" Emoji(\":smile:\")")]
    [InlineData("<http://x.y> www.x.com", "Autolink(\"<\"|\"http://x.y\"|\">\") \" \" Url(\"www.x.com\")")]
    [InlineData("a\nb", "\"a\" SoftBreak(\"\\n\") \"b\"")]
    [InlineData("a  \nb", "\"a\" HardBreak(\"  \"|\"\\n\") \"b\"")]
    [InlineData("a\\\nb", "\"a\" HardBreak(\"\\\"|\"\\n\") \"b\"")]
    [InlineData("**a", "\"**a\"")]
    [InlineData("**123***", "Strong(\"**\"|\"123\"|\"**\") \"*\"")]
    [InlineData("[t](", "\"[t](\"")]
    [InlineData("`a", "\"`a\"")]
    public void ParsesKindsAndSyntax(string source, string expected)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal(expected, InlineDump.Dump(tree));
    }

    [Fact]
    public void InlineLinkHasDestinationAndTitle()
    {
        var tree = Parser.Parse("x [*a*](<u v> 'ti') y", InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        var link = Assert.Single(tree.Nodes, static node => node.Kind == InlineKind.Link);
        Assert.Equal(LinkForm.Inline, link.LinkForm);
        Assert.Equal("u v", link.Value);
        Assert.Equal("ti", link.Title);
        Assert.Equal(new TextRange(3, 6), link.Content);
        Assert.Equal(new TextRange(8, 13), link.DestinationRange);
        Assert.Equal(new TextRange(14, 18), link.TitleRange);
        Assert.Equal("Link(\"[\"|Emphasis(\"*\"|\"a\"|\"*\")|\"](<u v> 'ti')\")", InlineDump.Dump(tree.Source, link));
    }

    [Theory]
    [InlineData("![al](s)", "Image(\"![\"|\"al\"|\"](s)\")")]
    [InlineData("[](u)", "Link(\"[\"|\"](u)\")")]
    [InlineData("[a\\]b](u)", "Link(\"[\"|\"a\" Escape(\"\\\"|\"]\") \"b\"|\"](u)\")")]
    public void ParsesLinkTextRanges(string source, string expected)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal(expected, InlineDump.Dump(tree));
    }

    [Theory]
    [InlineData("[r][Foo  Bar]", LinkForm.Full, "Foo  Bar", "Link(\"[\"|\"r\"|\"][Foo  Bar]\")")]
    [InlineData("[foo bar][]", LinkForm.Collapsed, "foo bar", "Link(\"[\"|\"foo bar\"|\"][]\")")]
    [InlineData("[FOO\nbar]", LinkForm.Shortcut, "FOO\nbar", "Link(\"[\"|\"FOO\" SoftBreak(\"\\n\") \"bar\"|\"]\")")]
    public void ReferenceLinksNeedADefinition(string source, LinkForm form, string label, string expected)
    {
        var undefined = Parser.Parse(source, InlineSyntaxSet.Full);
        Assert.DoesNotContain(undefined.Descendants(), static node => node.Kind == InlineKind.Link);
        Assert.Contains("foo bar", undefined.ReferenceCandidates);
        Assert.True(undefined.IsValidFor(static _ => false));
        Assert.False(undefined.IsValidFor(static label => label == "foo bar"));

        var tree = Parser.Parse(source, InlineSyntaxSet.Full, static label => label == "foo bar");
        InlineDump.AssertTiles(tree);
        var link = Assert.Single(tree.Nodes);
        Assert.Equal(form, link.LinkForm);
        Assert.Equal(label, link.Label);
        Assert.Equal(expected, InlineDump.Dump(tree));
        Assert.True(tree.IsValidFor(static label => label == "foo bar"));
    }

    [Theory]
    [InlineData("a[^1] b", "\"a\" FootnoteReference(\"[^\"|\"1\"|\"]\") \" b\"")]
    [InlineData("[^note x]", "FootnoteReference(\"[^\"|\"note x\"|\"]\")")]
    [InlineData("[^]", "\"[^]\"")]
    [InlineData("`[^1]`", "Code(\"`\"|\"[^1]\"|\"`\")")]
    public void FootnoteReferencesNeedNoDefinition(string source, string expected)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal(expected, InlineDump.Dump(tree));
    }

    [Theory]
    [InlineData("<u>x</u>", "HtmlSpan(\"<u>\"|\"x\"|\"</u>\")")]
    [InlineData("a<br>b<br/>", "\"a\" HtmlBreak(\"<br>\") \"b\" HtmlBreak(\"<br/>\")")]
    [InlineData("<KBD>Ctrl</KBD>", "HtmlSpan(\"<KBD>\"|\"Ctrl\"|\"</KBD>\")")]
    [InlineData("<u><sub>x</sub> **y**</u>", "HtmlSpan(\"<u>\"|HtmlSpan(\"<sub>\"|\"x\"|\"</sub>\") \" \" Strong(\"**\"|\"y\"|\"**\")|\"</u>\")")]
    [InlineData("<u>x", "Html(\"<u>\") \"x\"")]
    [InlineData("x</sup>", "\"x\" Html(\"</sup>\")")]
    [InlineData("<span>x</span>", "Html(\"<span>\") \"x\" Html(\"</span>\")")]
    [InlineData("<u>a <sup>b</u>", "HtmlSpan(\"<u>\"|\"a \" Html(\"<sup>\") \"b\"|\"</u>\")")]
    public void PairsWhitelistedHtml(string source, string expected)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal(expected, InlineDump.Dump(tree));
    }

    [Theory]
    [InlineData("**b** *i* $m$ ~s~ ^p^ `c`", "Strong(\"**\"|\"b\"|\"**\") \" \" Emphasis(\"*\"|\"i\"|\"*\") \" \" Math(\"$\"|\"m\"|\"$\") \" \" Subscript(\"~\"|\"s\"|\"~\") \" \" Superscript(\"^\"|\"p\"|\"^\") \" \" Code(\"`\"|\"c\"|\"`\")")]
    [InlineData("[t](u) <u>x</u> ==h== :smile: [^1]", "\"[t](u) <u>x</u> ==h== :smile: [^1]\"")]
    [InlineData("\\*", "Escape(\"\\\"|\"*\")")]
    public void FootnoteContentUsesTheTyporaSubset(string source, string expected)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.FootnoteContent);
        InlineDump.AssertTiles(tree);
        Assert.Equal(expected, InlineDump.Dump(tree));
    }

    [Theory]
    [InlineData("**a** [t](u)")]
    [InlineData("a\nb")]
    public void PlainTextIsOneTextNode(string source)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.PlainText);
        var node = Assert.Single(tree.Nodes);
        Assert.Equal(InlineKind.Text, node.Kind);
        Assert.Equal(new TextRange(0, source.Length), node.Range);
    }

    [Fact]
    public void EmptySourceHasNoNodes()
    {
        Assert.Empty(Parser.Parse(string.Empty, InlineSyntaxSet.Full).Nodes);
    }

    [Theory]
    [InlineData("1. not a list")]
    [InlineData("# not a heading")]
    [InlineData("> not a quote")]
    [InlineData("    not code")]
    public void BlockSyntaxStaysText(string source)
    {
        var tree = Parser.Parse(source, InlineSyntaxSet.Full);
        Assert.Equal("\"" + source + "\"", InlineDump.Dump(tree));
    }

    [Fact]
    public void ChinesePunctuationNextToMarkersFollowsCommonMark()
    {
        // CommonMark's flanking rules treat CJK punctuation as punctuation: "**中文，**后面" is not strong.
        var tree = Parser.Parse("**中文**后面 和**中文，**后面", InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal(InlineKind.Strong, tree.Nodes[0].Kind);
    }

    [Fact]
    public void NodesTouchingIncludesBothEndsAndNesting()
    {
        var tree = Parser.Parse("**a *b***c", InlineSyntaxSet.Full);
        InlineDump.AssertTiles(tree);
        Assert.Equal([InlineKind.Strong], tree.NodesTouching(0).Select(static node => node.Kind));
        Assert.Equal([InlineKind.Strong, InlineKind.Emphasis, InlineKind.Text], tree.NodesTouching(6).Select(static node => node.Kind));
        Assert.Equal([InlineKind.Strong, InlineKind.Text], tree.NodesTouching(9).Select(static node => node.Kind));
    }
}
