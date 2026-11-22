using Quill.Editor.Commands;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Selection rules on inline syntax (03-editor-state.md): markers follow a selection that covers a
/// collapsed element's whole content (Typora, Q18), and images are atomic for selections that leave
/// their block.
/// </summary>
public sealed class InlineSelectionTests
{
    [Theory]
    // "a **b** c": the element spans [2,7), its content [4,5).
    [InlineData(4, 5, "a <^>**b**<$> c")]
    [InlineData(5, 4, "a <$>**b**<^> c")]
    [InlineData(0, 5, "<^>a **b**<$> c")]
    [InlineData(4, 9, "a <^>**b** c<$>")]
    [InlineData(4, 4, "a **<|>b** c")]
    public void MarkersFollowWhenTheElementIsCollapsed(int anchor, int head, string expected)
    {
        var host = EditorTestHost.Load("<#p>a **b** c\n\n<#q>far<|>");
        Select(host, "p", anchor, head);
        Assert.Equal(expected + "\n\nfar", StateDsl.Dump(host.State));
    }

    [Fact]
    public void ExpandedElementsAreSelectedAsChosen()
    {
        // The caret touches the element, so its markers show: the user chose not to take the left "**".
        var host = EditorTestHost.Load("<#p>a **b<|>** c");
        Select(host, "p", 4, 9);
        Assert.Equal("a **<^>b** c<$>", StateDsl.Dump(host.State));
    }

    [Fact]
    public void PartialCoverDoesNotGrow()
    {
        var host = EditorTestHost.Load("<#p>a **bc** d\n\nfar<|>");
        Select(host, "p", 4, 5);
        Assert.Equal("a **<^>b<$>c** d\n\nfar", StateDsl.Dump(host.State));
    }

    [Fact]
    public void NestedElementsGrowTogether()
    {
        var host = EditorTestHost.Load("<#p>x ***a*** y\n\nfar<|>");
        Select(host, "p", 5, 6);
        Assert.Equal("x <^>***a***<$> y\n\nfar", StateDsl.Dump(host.State));
    }

    [Fact]
    public void EscapesFollowToo()
    {
        var host = EditorTestHost.Load("<#p>x\\*y\n\nfar<|>");
        Select(host, "p", 2, 3);
        Assert.Equal("x<^>\\*<$>y\n\nfar", StateDsl.Dump(host.State));
    }

    [Fact]
    public void PointerDownKeepsTheActiveSetOfThePress()
    {
        // Pressing the pointer keeps the elements as they were; the drag selects the collapsed element.
        var host = EditorTestHost.Load("<#p>a **b** c\n\nfar<|>");
        host.Execute(new SetInteraction(host.State.Interaction with { IsPointerDown = true }));
        Select(host, "p", 4, 4);
        Select(host, "p", 4, 5);
        host.Execute(new SetInteraction(host.State.Interaction with { IsPointerDown = false }));
        Assert.Equal("a <^>**b**<$> c\n\nfar", StateDsl.Dump(host.State));
    }

    [Fact]
    public void ImagesAreAtomicForSelectionsLeavingTheBlock()
    {
        var host = EditorTestHost.Load("<#p>a ![alt](i.png) b\n\n<#q>end<|>");
        host.Execute(new SetSelection(new TextSelection(new TextPosition(host.Id("p"), 5), new TextPosition(host.Id("q"), 2))));
        Assert.Equal("a <^>![alt](i.png) b\n\nen<$>d", StateDsl.Dump(host.State));

        // Within the block an active image's source can be selected in part.
        host.Execute(new SetSelection(new TextSelection(new TextPosition(host.Id("p"), 5), new TextPosition(host.Id("p"), 7))));
        Assert.Equal("a ![a<^>lt<$>](i.png) b\n\nend", StateDsl.Dump(host.State));
    }

    private static void Select(EditorTestHost host, string block, int anchor, int head) =>
        host.Execute(new SetSelection(new TextSelection(new TextPosition(host.Id(block), anchor), new TextPosition(host.Id(block), head))));
}
