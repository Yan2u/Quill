using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.Tests.Support;
using static Quill.Editor.Tests.Support.TestMarkdown;

namespace Quill.Editor.Tests.Markdown;

/// <summary>
/// Lists (02-document-model.md): structure, tight and loose export, numbering and generated syntax.
/// </summary>
public sealed class ListTests
{
    private readonly ModelBuilder _build = new();

    [Theory]
    [InlineData("- a\n- b\n", false)]
    [InlineData("- a\n\n- b\n", true)]
    [InlineData("- a\n- b\n\n  c\n", true)]
    [InlineData("1. a\n2. b\n", false)]
    public void LoosenessIsRecordedOnImport(string markdown, bool loose)
    {
        var list = Single<ListBlock>(RoundTrip(markdown));
        Assert.Equal(loose, list.Syntax!.WasLoose);
        Assert.Equal(loose, ListRules.IsLoose(list));
    }

    [Fact]
    public void BlocksAfterTheFirstChildAreHeadlessItems()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- a\n\n  b\n")).Children[0];
        Assert.Equal(["a", "b"], item.Children.Cast<Paragraph>().Select(paragraph => paragraph.Text));
    }

    [Fact]
    public void SubListIsAChildOfTheItem()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- a\n    - b\n")).Children[0];
        var sublist = Assert.IsType<ListBlock>(item.Children[1]);
        var subitem = (ListItem)sublist.Children[0];
        Assert.Equal("  ", subitem.Syntax!.Indent);
        Assert.Equal("b", ((Paragraph)subitem.Children[0]).Text);
    }

    [Fact]
    public void ItemOpeningWithABlankLine()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- \n  foo\n")).Children[0];
        Assert.True(item.Syntax!.OpensWithBlankLine);
        Assert.Equal("foo", Assert.IsType<Paragraph>(Assert.Single(item.Children)).Text);
    }

    [Fact]
    public void EmptyItemHoldsAnEmptyParagraph()
    {
        var list = Single<ListBlock>(RoundTrip("- a\n- \n- c\n"));
        var empty = Assert.IsType<Paragraph>(Assert.Single(((ListItem)list.Children[1]).Children));
        Assert.True(empty.IsEmpty);
    }

    // Typora: a marker needs a space or tab after it; alone on its line it is text.
    [Theory]
    [InlineData("-\n")]
    [InlineData("*\n")]
    [InlineData("+")]
    [InlineData("1.\n")]
    [InlineData("1)\n")]
    public void MarkerAloneIsText(string markdown)
    {
        Assert.Equal(markdown.TrimEnd('\n'), Single<Paragraph>(RoundTrip(markdown)).Text);
    }

    [Theory]
    [InlineData("-\n  foo\n", "-\n  foo")]
    [InlineData("- a\n-\n- c\n", "a\n-")]
    public void MarkerAloneDoesNotStartAnItem(string markdown, string firstText)
    {
        var document = RoundTrip(markdown);
        var first = document.Children[0] is ListBlock list ? ((ListItem)list.Children[0]).Children[0] : document.Children[0];
        Assert.Equal(firstText, Assert.IsType<Paragraph>(first).Text);
    }

    [Theory]
    [InlineData("- \n")]
    [InlineData("1. \n")]
    [InlineData("-\t\n")]
    public void MarkerWithSpaceIsAnEmptyItem(string markdown)
    {
        var item = (ListItem)Assert.Single(Single<ListBlock>(RoundTrip(markdown)).Children);
        Assert.True(Assert.IsType<Paragraph>(Assert.Single(item.Children)).IsEmpty);
    }

    // Typora keeps the blocks of an item whose text is empty (CommonMark ends an item at a blank line
    // when nothing followed its marker): the blank lines after the marker line are empty paragraphs.
    [Theory]
    [InlineData("- \n\n  b\n", "Paragraph \"\"|Paragraph \"b\"")]
    [InlineData("- \n\n\n  b\n", "Paragraph \"\"|Paragraph \"\"|Paragraph \"b\"")]
    [InlineData("- \n\n    - b\n", "Paragraph \"\"|List bullet")]
    [InlineData("- \n\n      code\n", "Paragraph \"\"|CodeBlock info=\"\" \"code\"")]
    public void EmptyItemKeepsTheBlocksAfterIt(string markdown, string children)
    {
        var item = (ListItem)Assert.Single(Single<ListBlock>(RoundTrip(markdown)).Children);
        Assert.Equal(children, string.Join('|', item.Children.Select(static child => ModelDump.Structure(child).Split('\n')[0].Trim())));
    }

    // The blank line after the empty marker line does not make a list with an empty item loose.
    [Fact]
    public void EmptyItemWithSubListStaysTight()
    {
        Assert.False(ListRules.IsLoose(Single<ListBlock>(RoundTrip("- \n\n    - b\n"))));
    }

    // A task item's empty text shares the marker line: the blank line after it is a separator.
    [Fact]
    public void EmptyTaskItemKeepsTheBlocksAfterIt()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- [ ] \n\n  b\n")).Children[0];
        Assert.Equal(TaskState.Unchecked, item.Task);
        Assert.Equal(["", "b"], item.Children.Cast<Paragraph>().Select(static paragraph => paragraph.Text));
    }

    [Fact]
    public void EmptyItemInTheMiddleKeepsItsBlocks()
    {
        var list = Single<ListBlock>(RoundTrip("- a\n- \n\n  b\n- c\n"));
        Assert.Equal(3, list.Children.Count);
        Assert.Equal(["", "b"], ((ListItem)list.Children[1]).Children.Cast<Paragraph>().Select(static paragraph => paragraph.Text));
    }

    // Markdig also ends an item opening with a blank line at a blank line after its content; CommonMark
    // and Typora keep the content that follows in the item.
    [Fact]
    public void ItemOpeningWithABlankLineContinuesAfterBlankLines()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- \n  foo\n\n  bar\n")).Children[0];
        Assert.True(item.Syntax!.OpensWithBlankLine);
        Assert.Equal(["foo", "bar"], item.Children.Cast<Paragraph>().Select(static paragraph => paragraph.Text));
    }

    // Unindented text after an empty item still ends the list.
    [Fact]
    public void UnindentedTextAfterEmptyItemEndsTheList()
    {
        var document = RoundTrip("- \n\nb\n");
        Assert.Equal([BlockKind.List, BlockKind.Paragraph], document.Children.Select(static block => block.Kind));
    }

    // Typora: an empty item interrupts a paragraph (CommonMark makes the line paragraph text, or a Setext
    // underline under "- a"). An ordered list not starting at 1 still cannot (Q56).
    [Theory]
    [InlineData("- a\n    - \n", "Paragraph|List")]
    [InlineData("- a\n  - \n", "Paragraph|List")]
    public void EmptySubItemInterruptsTheParagraph(string markdown, string children)
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip(markdown)).Children[0];
        Assert.Equal(children, string.Join('|', item.Children.Select(static child => child.Kind)));
    }

    [Theory]
    [InlineData("a\n- \n", true)]
    [InlineData("a\n1. \n", true)]
    [InlineData("a\n2. \n", false)]
    [InlineData("a\n-\n", false)]
    public void EmptyItemAfterParagraph(string markdown, bool list)
    {
        var document = RoundTrip(markdown);
        Assert.Equal(list, document.Children.Count == 2 && document.Children[1] is ListBlock);
    }

    // CommonMark: an item takes exactly its content width from the lines it continues, so an item
    // nested one column into its parent's content does not take in the next, shallower list. Markdig
    // measured the nested item from varying columns and nested that list into it.
    [Fact]
    public void ListAfterIndentedSubItemIsItsSibling()
    {
        var item = (ListItem)Single<ListBlock>(RoundTrip("- x\n\n   - y\n\n    * z\n")).Children[0];
        Assert.Equal([BlockKind.Paragraph, BlockKind.List, BlockKind.List], item.Children.Select(static child => child.Kind));
    }

    [Fact]
    public void TaskMarkersAreItemState()
    {
        var list = Single<ListBlock>(RoundTrip("- [ ] todo\n- [x] done\n- [ ]\n"));
        var items = list.Children.Cast<ListItem>().ToList();
        Assert.Equal([TaskState.Unchecked, TaskState.Checked, TaskState.None], items.Select(item => item.Task));
        Assert.Equal(["todo", "done", "[ ]"], items.Select(item => ((Paragraph)item.Children[0]).Text));
        Assert.Equal("[ ] ", items[0].Syntax!.TaskMarker);
    }

    [Theory]
    [InlineData("1. a\n2. b\n3. c\n", ListNumbering.Increasing)]
    [InlineData("1. a\n1. b\n1. c\n", ListNumbering.Same)]
    [InlineData("3. a\n7. b\n2. c\n", ListNumbering.Other)]
    [InlineData("1. a\n", ListNumbering.Increasing)]
    [InlineData("5. a\n", ListNumbering.Same)]
    public void NumberingStylesRoundTrip(string markdown, ListNumbering numbering)
    {
        Assert.Equal(numbering, ListRules.ClassifyNumbering(Single<ListBlock>(RoundTrip(markdown))));
    }

    [Fact]
    public void OrderedMarkerKeepsLeadingZerosAndDelimiter()
    {
        var list = Single<ListBlock>(RoundTrip("007) a\n008) b\n"));
        Assert.Equal(')', list.MarkerChar);
        Assert.Equal([7, 8], list.Children.Cast<ListItem>().Select(item => item.Number));
    }

    [Fact]
    public void RenumberingRewritesMarkersAndReindentsWiderItems()
    {
        var document = Import("8. a\n9. b\n   continued\n");
        var renumbered = MapItems(document, (item, index) => item with { Number = 9 + index });
        Assert.Equal("9. a\n10. b\n    continued\n", Serialize(renumbered));
    }

    [Fact]
    public void ToggledTaskKeepsTheRestOfTheMarker()
    {
        var document = Import("- [ ]\ttodo\n");
        Assert.Equal("- [x]\ttodo\n", Serialize(MapItems(document, (item, _) => item with { Task = TaskState.Checked })));
    }

    [Fact]
    public void AddingAHeadlessItemToATightListRewritesAllSeparators()
    {
        var document = Import("- a\n  - b\n- c\n");
        var list = (ListBlock)document.Children[0];
        var last = (ListItem)list.Children[1];
        var changed = document with
        {
            Children = [list with { Children = list.Children.SetItem(1, last with { Children = last.Children.Add(_build.Paragraph("new")) }) }],
        };
        Assert.Equal("- a\n\n  - b\n\n- c\n\n  new\n", Serialize(changed));
    }

    [Fact]
    public void NewItemsFollowTheListsMarkerCharacter()
    {
        var document = Import("* a\n");
        var list = (ListBlock)document.Children[0];
        var changed = document with { Children = [list with { Children = list.Children.Add(_build.Item("b")) }] };
        Assert.Equal("* a\n* b\n", Serialize(changed));
    }

    [Fact]
    public void AdjacentListsOfTheSameTypeSwitchMarkers()
    {
        var document = _build.Document(
            _build.Bullets(_build.Item("a")),
            _build.Bullets(_build.Item("b")),
            _build.Bullets(_build.Item("c")),
            _build.Numbered(_build.NumberedItem(1, _build.Paragraph("d"))),
            _build.Numbered(_build.NumberedItem(1, _build.Paragraph("e"))));
        Assert.Equal("- a\n\n* b\n\n- c\n\n1. d\n\n1) e\n", SerializeStable(document));
    }

    [Fact]
    public void WithoutMarkerSwitchingAdjacentListsMergeWhenReopened()
    {
        var document = _build.Document(_build.Bullets(_build.Item("a")), _build.Paragraph(string.Empty), _build.Bullets(_build.Item("b")));
        var pair = Assert.Single(ListRules.FindMergingAdjacentLists(document));
        Assert.Same(document.Children[0], pair.First);
        string markdown = Serialize(document, new EditorOptions { SwitchMarkerForAdjacentLists = false });
        Assert.Equal("- a\n\n\n- b\n", markdown);
        Assert.Single(Import(markdown).Children);
    }

    [Fact]
    public void ImportedAdjacentListsUseDifferentMarkersAndDoNotMerge()
    {
        Assert.Empty(ListRules.FindMergingAdjacentLists(RoundTrip("- a\n\n* b\n\n1. c\n2) d\n")));
    }

    [Fact]
    public void GeneratedSubListsAreIndentedFourColumnsPerLevel()
    {
        var document = _build.Document(_build.Bullets(
            _build.Item(_build.Paragraph("a"), _build.Bullets(
                _build.Item(_build.Paragraph("b"), _build.Bullets(_build.Item("c")))))));
        Assert.Equal("- a\n    - b\n        - c\n", SerializeStable(document));
    }

    [Theory]
    [InlineData(1, "1. a\n    - b\n")]
    [InlineData(10, "10. a\n    - b\n")]
    [InlineData(100, "100. a\n     - b\n")]
    public void GeneratedSubListUnderAWideNumberAlignsWithTheContent(int number, string expected)
    {
        var document = _build.Document(_build.Numbered(
            _build.NumberedItem(number, _build.Paragraph("a"), _build.Bullets(_build.Item("b")))));
        Assert.Equal(expected, SerializeStable(document));
    }

    [Fact]
    public void GeneratedHeadlessItemIsIndentedToTheContentColumn()
    {
        var document = _build.Document(_build.Bullets(_build.Item(_build.Paragraph("a"), _build.Paragraph("b"))));
        Assert.Equal("- a\n\n  b\n", SerializeStable(document));
    }

    [Fact]
    public void GeneratedCodeInAnItemKeepsItsCode()
    {
        var document = _build.Document(_build.Bullets(_build.Item(_build.Code("x = 1\n  y", "py"))));
        Assert.Equal("- ```py\n  x = 1\n    y\n  ```\n", SerializeStable(document));
    }

    [Fact]
    public void GeneratedTasksAndNumbers()
    {
        var document = _build.Document(
            _build.Bullets(_build.Task(false, "todo"), _build.Task(true, "done")),
            _build.Paragraph("between"),
            _build.Numbered(_build.NumberedItem(1, _build.Paragraph("one")), _build.NumberedItem(2, _build.Paragraph("two"))));
        Assert.Equal("- [ ] todo\n- [x] done\n\nbetween\n\n1. one\n2. two\n", SerializeStable(document));
    }

    [Fact]
    public void GeneratedRuleAvoidsReadingAsSomethingElse()
    {
        var document = _build.Document(_build.Rule(), _build.Bullets(_build.Item(_build.Rule())), _build.Rule());
        Assert.Equal("***\n\n- ***\n\n---\n", SerializeStable(document));
    }

    private static Document MapItems(Document document, Func<ListItem, int, ListItem> map)
    {
        var list = (ListBlock)document.Children[0];
        var items = list.Children.Cast<ListItem>().Select(map).Cast<Block>();
        return document with { Children = [list with { Children = [.. items] }] };
    }
}
