using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// Projection of text leaves (05-projection-and-view.md, 10-elements.md): runs for the block controls,
/// active elements showing their syntax, and the position map.
/// </summary>
public sealed class InlineProjectionTests
{
    [Theory]
    [InlineData("<#p>a **b** c\n\n<|>", "\"a \" Strong\"b\" \" c\"")]
    [InlineData("<#p>a **b<|>** c", "\"a \" Strong<**> Strong\"b\" Strong<**> \" c\"")]
    [InlineData("<#p>a <|>**b** c", "\"a \" Strong<**> Strong\"b\" Strong<**> \" c\"")]
    [InlineData("<#p>a **b**<|> c", "\"a \" Strong<**> Strong\"b\" Strong<**> \" c\"")]
    [InlineData("<#p>a<|> **b** c", "\"a \" Strong\"b\" \" c\"")]
    [InlineData("<#p>***a<|>***", "Emphasis<*> Strong+Emphasis<**> Strong+Emphasis\"a\" Strong+Emphasis<**> Emphasis<*>")]
    [InlineData("<#p>x **a** *b<|>*", "\"x \" Strong\"a\" \" \" Emphasis<*> Emphasis\"b\" Emphasis<*>")]
    [InlineData("<#p>**abc\n\n<|>", "\"**abc\"")]
    [InlineData("<#p>x\\*<|>", "\"x\" Escape<\\> \"*\"")]
    [InlineData("<#p>x\\*y\n\n<|>", "\"x*y\"")]
    [InlineData("<#p>[t](u)\n\n<|>", "Link\"t\"")]
    [InlineData("<#p>[t](u<|>)", "Link<[> Link\"t\" Link<](u)>")]
    [InlineData("<#p>a\nb\n\n<|>", "\"a\" NL \"b\"")]
    [InlineData("<#p>$x^2$\n\n<|>", "Math[x^2]")]
    [InlineData("<#p>$x^2<|>$", "Math<$> Math\"x^2\" Math<$>")]
    [InlineData("<#p>![alt](i.png)\n\n<|>", "Image[alt]")]
    [InlineData("<#p>![alt](i.png)<|>", "Image<![> \"alt\" Image<](i.png)> Image[alt]")]
    [InlineData("<#p>:smile: x\n\n<|>", "Emoji[😄] \" x\"")]
    [InlineData("<#p>:smile:<|> x", "Emoji[😄] Emoji<:smile:> \" x\"")]
    [InlineData("<#p>&copy;\n\n<|>", "Entity[©]")]
    [InlineData("<#p>&copy;<|>", "Entity<&copy;>")]
    [InlineData("<#p>a[^1]\n\n<|>", "\"a\" FootnoteReference\"1\"")]
    [InlineData("<#p>a[^1]<|>", "\"a\" FootnoteReference<[^> FootnoteReference\"1\" FootnoteReference<]>")]
    [InlineData("<#p><u>x</u> a<br>b\n\n<|>", "Underline\"x\" \" a\" NL \"b\"")]
    [InlineData("<#p><u>x</u><|> a<br>b", "Underline<<u>> Underline\"x\" Underline<</u>> \" a\" NL \"b\"")]
    [InlineData("<#p>a<br><|>b", "\"a\" <<br>> \"b\"")]
    [InlineData("<#p><span>x</span>\n\n<|>", "Html\"<span>\" \"x\" Html\"</span>\"")]
    [InlineData("<#p>www.x.com\n\n<|>", "Link\"www.x.com\"")]
    public void RunsShowActiveSyntax(string dsl, string expected)
    {
        var host = EditorTestHost.Load(dsl);
        var paragraph = ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("p")));
        Assert.Equal(expected, ProjectionDump.Runs((InlineContent)ProjectionDump.Attribute(paragraph, ProjectionNames.Content)!));
    }

    [Fact]
    public void ActiveMathHasAPreview()
    {
        var host = EditorTestHost.Load("<#p>a $x<|>$ b");
        var content = (InlineContent)ProjectionDump.Attribute(ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("p"))), ProjectionNames.Content)!;
        var preview = Assert.Single(content.Previews);
        Assert.Equal("x", preview.Value);
        Assert.Equal(new Quill.Editor.Inlines.TextRange(2, 5), preview.Display);
    }

    [Fact]
    public void HiddenMarkersMapToOneDisplayPosition()
    {
        // "a **b** c" with the caret elsewhere: the markers are hidden.
        var host = EditorTestHost.Load("<#p>a **b** c\n\n<|>");
        var map = host.Project().Map[host.Id("p")]!;
        Assert.Equal("C[0,2)>[0,2) H[2,4)>[2,2) C[4,5)>[2,3) H[5,7)>[3,3) C[7,9)>[3,5)", ProjectionDump.Segments(map));
        Assert.Equal(5, map.DisplayLength);

        // Source to display: everything in hidden markers collapses.
        Assert.Equal(2, map.ToDisplay(3, Affinity.Forward));
        Assert.Equal(3, map.ToDisplay(6, Affinity.Backward));

        // Display to source: **b|** versus **b**| (03-editor-state.md).
        Assert.Equal(5, map.ToSource(3, Affinity.Backward));
        Assert.Equal(7, map.ToSource(3, Affinity.Forward));
        Assert.Equal(2, map.ToSource(2, Affinity.Backward));
        Assert.Equal(4, map.ToSource(2, Affinity.Forward));
    }

    [Fact]
    public void AtomicObjectsHaveNoInnerPositions()
    {
        var host = EditorTestHost.Load("<#p>a $x$ b\n\n<|>");
        var map = host.Project().Map[host.Id("p")]!;
        Assert.Equal("C[0,2)>[0,2) A[2,5)>[2,3) C[5,7)>[3,5)", ProjectionDump.Segments(map));
        Assert.Equal(2, map.ToDisplay(3, Affinity.Backward));
        Assert.Equal(3, map.ToDisplay(3, Affinity.Forward));
        Assert.Equal(2, map.ToSource(2, Affinity.Backward));
        Assert.Equal(5, map.ToSource(3, Affinity.Forward));
    }

    [Fact]
    public void DisplayOnlyObjectsSitBetweenTheirNeighbours()
    {
        // An active emoji shows the emoji and then its shortcode (10-elements.md).
        var host = EditorTestHost.Load("<#p>x :smile:<|>");
        var map = host.Project().Map[host.Id("p")]!;
        Assert.Equal("C[0,2)>[0,2) A[2,2)>[2,3) M[2,9)>[3,10)", ProjectionDump.Segments(map));
        Assert.Equal(2, map.ToDisplay(2, Affinity.Backward));
        Assert.Equal(3, map.ToDisplay(2, Affinity.Forward));
    }

    [Fact]
    public void LeadingZeroWidthSpaceIsHidden()
    {
        var host = EditorTestHost.Load("<#p>​\tcode-like\n\n<|>");
        var map = host.Project().Map[host.Id("p")]!;
        Assert.Equal(SegmentKind.Hidden, map.Segments[0].Kind);
        Assert.Equal(new Quill.Editor.Inlines.TextRange(0, 1), map.Segments[0].Source);
        Assert.Equal(0, map.ToDisplay(1, Affinity.Forward));
    }

    [Fact]
    public void FootnoteContentAndPlainSlotsUseTheirSyntaxSets()
    {
        var host = EditorTestHost.Load("[^n]: **b** [t](u)\n\n[r]: /url \"**t**\"\n\n<|>");
        var result = host.Project();
        var slots = DescendantsOfType(result.Root, ProjectionNames.DefinitionSlot).ToList();
        string Runs(int i) => ProjectionDump.Runs((InlineContent)ProjectionDump.Attribute(slots[i], ProjectionNames.Content)!);
        Assert.Equal("\"n\"", Runs(0));
        Assert.Equal("Strong\"b\" \" [t](u)\"", Runs(1));
        Assert.Equal("\"**t**\"", Runs(4));
    }

    [Fact]
    public void SelectionAndCompositionAreInDisplayOffsets()
    {
        var host = EditorTestHost.Load("<#p>a **b** <^>c d<$>");
        var paragraph = ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("p")));
        Assert.Equal(4, ProjectionDump.Attribute(paragraph, ProjectionNames.SelectionStart));
        Assert.Equal(7, ProjectionDump.Attribute(paragraph, ProjectionNames.SelectionEnd));
    }

    internal static IEnumerable<VariableXaml.Model.VNode> DescendantsOfType(VariableXaml.Model.VNode node, VariableXaml.Model.XamlName type)
    {
        if (node.Type == type)
        {
            yield return node;
        }

        foreach (var child in node.Children)
        {
            foreach (var found in DescendantsOfType(child, type))
            {
                yield return found;
            }
        }
    }
}
