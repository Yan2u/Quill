using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.Editor.Tests.Support;
using Quill.VariableXaml.Model;

namespace Quill.Editor.Tests.Projection;

/// <summary>
/// Projection of the block kinds (05-projection-and-view.md, 10-elements.md): element controls,
/// semantic attributes, virtual paragraphs and whole-block selection. Nodes carry no style values.
/// </summary>
public sealed class BlockProjectionTests
{
    [Fact]
    public void DocumentHoldsTheBlocksInAVirtualStack()
    {
        var host = EditorTestHost.Load("# T<|>\n\npara");
        var result = host.Project();
        Assert.Equal(ProjectionNames.Document, result.Root.Type);
        Assert.Equal(ProjectionNames.VirtualStack, result.Root.Children[0].Type);
        Assert.Equal([ProjectionNames.Heading, ProjectionNames.Paragraph], ProjectionDump.Blocks(result).Select(static node => node.Type));
        Assert.All(ProjectionDump.Blocks(result), static node => Assert.StartsWith("b", node.Key));
    }

    [Fact]
    public void HeadingShowsItsLevelAndFocus()
    {
        var host = EditorTestHost.Load("<#h>## Ti<|>tle\n\n<#p>para");
        var result = host.Project();
        var heading = ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("h")));
        Assert.Equal(2, ProjectionDump.Attribute(heading, ProjectionNames.Level));
        Assert.Equal(true, ProjectionDump.Attribute(heading, ProjectionNames.IsFocused));
        Assert.Null(ProjectionDump.Attribute(ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("p"))), ProjectionNames.IsFocused));
    }

    [Fact]
    public void RawBlocksCarryTheirText()
    {
        var host = EditorTestHost.Load("<#c>```js\nlet x;\n```\n\n<#m>$$\nx^2\n$$\n\n---\n\n<|>");
        var result = host.Project();
        // A code block is a container of its code and its language (Q91); the code carries the language.
        Assert.Equal(ProjectionNames.CodeFence, ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("c"))).Type);
        var fence = (CodeFence)DocumentTree.Find(host.State.Document, host.Id("c"));
        var code = ProjectionDump.Find(result, DocumentProjector.KeyOf(fence.Code.Id));
        Assert.Equal(ProjectionNames.CodeBlock, code.Type);
        Assert.Equal(ProjectionNames.CodeLanguage, ProjectionDump.Find(result, DocumentProjector.KeyOf(fence.Language.Id)).Type);
        Assert.Equal("js", ProjectionDump.Attribute(code, ProjectionNames.Info));
        Assert.Equal("let x;", ProjectionDump.Attribute(code, ProjectionNames.Text));

        // Unfocused display math is one object; focused, its source shows (10-elements.md).
        Assert.Equal(SegmentKind.Atomic, Assert.Single(result.Map[host.Id("m")]!.Segments).Kind);
        host.Execute(new SetSelection(State.TextSelection.Caret(host.Id("m"), 1)));
        Assert.Equal(SegmentKind.Content, host.Project().Map[host.Id("m")]!.Segments[0].Kind);
    }

    [Fact]
    public void ListsNumberFromTheFirstItemAndNest()
    {
        var host = EditorTestHost.Load("3. a\n3. b\n    - [x] c\n\n<|>");
        var result = host.Project();
        var list = ProjectionDump.Blocks(result)[0];
        Assert.Equal(ProjectionNames.List, list.Type);
        Assert.Equal(true, ProjectionDump.Attribute(list, ProjectionNames.IsOrdered));
        Assert.Equal(1, ProjectionDump.Attribute(list, ProjectionNames.Level));
        Assert.Equal([3, 4], list.Children.Select(static item => (int)ProjectionDump.Attribute(item, ProjectionNames.Number)!));
        var nested = list.Children[1].Children[1];
        Assert.Equal(2, ProjectionDump.Attribute(nested, ProjectionNames.Level));
        Assert.Equal(TaskState.Checked, ProjectionDump.Attribute(nested.Children[0], ProjectionNames.Task));
    }

    [Fact]
    public void TablesCarryAlignmentsAndTheHeaderRow()
    {
        var host = EditorTestHost.Load("| a | b |\n| :-: | -: |\n| 1 | 2<|> |");
        var table = ProjectionDump.Blocks(host.Project())[0];
        Assert.Equal("Center,Right", ProjectionDump.Attribute(table, ProjectionNames.Alignments));
        Assert.Equal(true, ProjectionDump.Attribute(table, ProjectionNames.IsActive));
        Assert.Equal(true, ProjectionDump.Attribute(table.Children[0], ProjectionNames.IsHeader));
        Assert.Null(ProjectionDump.Attribute(table.Children[1], ProjectionNames.IsHeader));
        Assert.Equal(TableAlignment.Right, ProjectionDump.Attribute(table.Children[1].Children[1], ProjectionNames.Alignment));
    }

    [Fact]
    public void FirstFootnoteDefinitionGetsTheBackLinks()
    {
        var host = EditorTestHost.Load("a[^n] b[^N] `[^n]`<|>\n\n<#f>[^n]: one\n\n<#g>[^n]: two");
        var result = host.Project();
        Assert.Equal(2, ProjectionDump.Attribute(ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("f"))), ProjectionNames.BacklinkCount));
        Assert.Null(ProjectionDump.Attribute(ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("g"))), ProjectionNames.BacklinkCount));

        host.Type(" c[^n]");
        Assert.Equal(3, ProjectionDump.Attribute(ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("f"))), ProjectionNames.BacklinkCount));
    }

    [Fact]
    public void DefinitionIsActiveWhileFocusedInASlot()
    {
        var host = EditorTestHost.Load("<#d>[x]: /u<|>\n\npara");
        var definition = ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("d")));
        Assert.Equal(ProjectionNames.LinkReferenceDefinition, definition.Type);
        Assert.Equal(true, ProjectionDump.Attribute(definition, ProjectionNames.IsActive));
        Assert.Equal(
            [DefinitionSlotKind.LinkLabel, DefinitionSlotKind.LinkDestination, DefinitionSlotKind.LinkTitle],
            definition.Children.Select(static slot => (DefinitionSlotKind)ProjectionDump.Attribute(slot, ProjectionNames.SlotKind)!));
    }

    [Fact]
    public void VirtualParagraphAppearsAtTheCaretGap()
    {
        var host = EditorTestHost.Load("```\na\n```\n\n<gap>\n\n```\nb\n```");
        var blocks = ProjectionDump.Blocks(host.Project());
        Assert.Equal([ProjectionNames.CodeFence, ProjectionNames.VirtualParagraph, ProjectionNames.CodeFence], blocks.Select(static node => node.Type));
        Assert.Equal($"gap-{blocks[2].Key}", blocks[1].Key);

        host.Press("Up");
        Assert.DoesNotContain(ProjectionDump.Blocks(host.Project()), static node => node.Type == ProjectionNames.VirtualParagraph);
    }

    [Fact]
    public void WholeBlocksInTheSelectionAreSelected()
    {
        var host = EditorTestHost.Load("<#a>a<^>bc\n\n<#r>---\n\n<#b>de<$>f");
        var result = host.Project();
        Assert.Equal(true, ProjectionDump.Attribute(ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("r"))), ProjectionNames.IsSelected));
        var first = ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("a")));
        Assert.Equal(1, ProjectionDump.Attribute(first, ProjectionNames.SelectionStart));
        Assert.Equal(3, ProjectionDump.Attribute(first, ProjectionNames.SelectionEnd));
        var last = ProjectionDump.Find(result, DocumentProjector.KeyOf(host.Id("b")));
        Assert.Equal(0, ProjectionDump.Attribute(last, ProjectionNames.SelectionStart));
        Assert.Equal(2, ProjectionDump.Attribute(last, ProjectionNames.SelectionEnd));
        Assert.Equal($"{host.Id("a")}:1..{host.Id("b")}:2", result.Selection.ToString());
    }

    [Fact]
    public void NodesHoldNoStyleValues()
    {
        var host = EditorTestHost.Load("# **T**<|>\n\n- a\n\n> q\n\n| a |\n| - |\n| b |\n\n```\nc\n```");
        string markup = ProjectionDump.Markup(host.Project().Root);
        foreach (string style in new[] { "FontSize", "Foreground", "Background", "Margin", "Padding", "FontWeight", "FontFamily" })
        {
            Assert.DoesNotContain(style, markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ContentHashIsStructural()
    {
        var host = EditorTestHost.Load("<#p>a **b**<|>");
        var first = (InlineContent)ProjectionDump.Attribute(ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("p"))), ProjectionNames.Content)!;
        var fresh = (InlineContent)ProjectionDump.Attribute(ProjectionDump.Find(new DocumentProjector().Project(host.State), DocumentProjector.KeyOf(host.Id("p"))), ProjectionNames.Content)!;
        Assert.NotSame(first, fresh);
        Assert.Equal(first, fresh);
        Assert.Equal(first.StructuralHash, fresh.StructuralHash);
        Assert.Equal(NodeHash.Combine(NodeHash.Seed, (object)first), NodeHash.Combine(NodeHash.Seed, (object)fresh));

        host.Type("x");
        var changed = (InlineContent)ProjectionDump.Attribute(ProjectionDump.Find(host.Project(), DocumentProjector.KeyOf(host.Id("p"))), ProjectionNames.Content)!;
        Assert.NotEqual(first.StructuralHash, changed.StructuralHash);
    }
}
