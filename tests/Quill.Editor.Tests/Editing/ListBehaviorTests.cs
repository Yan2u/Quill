using Quill.Editor.Documents;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// List behaviors of 04-commands-and-behaviors.md ("Typora 行为清单" and "列表的级别与 Tab"),
/// including examples 1 to 5 as written there (the caret is <c>&lt;|&gt;</c>).
/// </summary>
public sealed class ListBehaviorTests
{
    private static string Run(string before, string keys)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        return host.Dump();
    }

    // Example 1: no parent item can be found, so Tab types a tab. The tab at the start of the item's
    // first line gets the leading zero-width space (Q28, Q34).
    [Fact]
    public void Example1TabWithoutParentTypesTab()
    {
        Assert.Equal(
            "1. ​\t<|>the first item\n2. the second",
            Run("1. <|>the first item\n2. the second", "Tab"));
    }

    // Example 2: the normal case.
    [Fact]
    public void Example2TabNestsUnderPreviousItem()
    {
        Assert.Equal(
            "1. the first item\n    1. the <|>second",
            Run("1. the first item\n2. the <|>second", "Tab"));
    }

    // Example 3: no parent either, judged by the levels after indenting.
    [Fact]
    public void Example3TabWithoutParentAfterIndentingTypesTab()
    {
        Assert.Equal(
            "- one\n    - nested one\t<|>\n        - nested square one\n    - nested two\n- two\n- three",
            Run("- one\n    - nested one<|>\n        - nested square one\n    - nested two\n- two\n- three", "Tab"));
    }

    // Example 4: the children follow their parent's level; nested two changes parent and stays.
    [Fact]
    public void Example4ShiftTabMovesChildrenAlong()
    {
        Assert.Equal(
            "- one\n- nested one<|>\n    - nested square one\n    - nested two\n- two\n- three",
            Run("- one\n    - nested one<|>\n        - nested square one\n    - nested two\n- two\n- three", "Shift+Tab"));
    }

    // Example 5: Shift+Tab on a level-1 item makes a paragraph, with blank lines around it.
    [Fact]
    public void Example5ShiftTabAtLevelOneMakesParagraph()
    {
        Assert.Equal(
            "- one\n\nnested one<|>\n\n- nested square one\n- nested two\n- two\n- three",
            Run("- one\n- nested one<|>\n    - nested square one\n    - nested two\n- two\n- three", "Shift+Tab"));
    }

    // Tab anywhere in the item behaves the same.
    [Theory]
    [InlineData("- a\n- <|>b", "- a\n    - <|>b")]
    [InlineData("- a\n- b<|>", "- a\n    - b<|>")]
    [InlineData("- a\n    - b\n- c<|>", "- a\n    - b\n    - c<|>")]
    [InlineData("- a\n- b<|>\n    - c", "- a\n    - b<|>\n        - c")]
    public void TabIndentsItemWithItsChildren(string before, string after) => Assert.Equal(after, Run(before, "Tab"));

    [Theory]
    [InlineData("- a\n    - b<|>", "- a\n- b<|>")]
    [InlineData("- a\n    - b<|>\n    - c", "- a\n- b<|>\n    - c")]
    [InlineData("- <|>a", "<|>a")]
    [InlineData("- a\n- <|>b\n- c", "- a\n\n<|>b\n\n- c")]
    public void ShiftTabOutdents(string before, string after) => Assert.Equal(after, Run(before, "Shift+Tab"));

    // Headless items: Tab types a tab; Shift+Tab goes up one level unconditionally and stays headless (Q21).
    [Theory]
    [InlineData("- a\n\n  b<|>", "Tab", "- a\n\n  b\t<|>")]
    [InlineData("- a\n    - b\n\n      c<|>", "Shift+Tab", "- a\n    - b\n\n  c<|>")]
    [InlineData("- a\n\n  b<|>", "Shift+Tab", "- a\n\nb<|>")]
    public void HeadlessItemTabAndShiftTab(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // Non-empty item: Enter splits it; ordered numbers count on, the task state carries over. At the
    // start of the item it is the same split: the item stays (empty) and its text moves to a new item
    // below, with the caret (Typora).
    [Theory]
    [InlineData("- a<|>b", "- a\n- <|>b")]
    [InlineData("- ab<|>", "- ab\n- <|>")]
    [InlineData("- a\n- <|>b", "- a\n- <empty>\n- <|>b")]
    [InlineData("1. a<|>\n2. b", "1. a\n2. <|>\n3. b")]
    [InlineData("1. a<|>\n1. b", "1. a\n1. <|>\n1. b")]
    [InlineData("1. a<|>\n5. b", "1. a\n2. <|>\n3. b")]
    [InlineData("3. a<|>", "3. a\n3. <|>")]
    [InlineData("- [x] a<|>", "- [x] a\n- [x] <|>")]
    [InlineData("- [ ] a<|>b", "- [ ] a\n- [ ] <|>b")]
    [InlineData("- a<|>\n    - b", "- a\n- <|>\n    - b")]
    public void EnterInNonEmptyItemSplitsIt(string before, string after) => Assert.Equal(after, Run(before, "Enter"));

    // Empty item: Enter leaves the list, splitting it around the new empty paragraph; nested, it goes
    // up one level as a headless item.
    [Theory]
    [InlineData("- a\n- <|>", "- a\n\n<|>")]
    [InlineData("- a\n- <|>\n- b", "- a\n\n<|>\n\n- b")]
    [InlineData("- a\n    - <|>", "- a\n\n  <|>")]
    [InlineData("- a\n    - b\n    - <|>", "- a\n\n    - b\n\n  <|>")]
    public void EnterInEmptyItemLeavesList(string before, string after) => Assert.Equal(after, Run(before, "Enter"));

    // Backspace at the start of an item: the first item, or one after an empty item, is merged into
    // the previous row (a paragraph without one); any other item becomes headless.
    [Theory]
    [InlineData("- <|>a\n- b", "<|>a\n\n- b")]
    [InlineData("- a\n    - <|>b", "- a<|>b")]
    [InlineData("- <empty>\n- <|>b", "- <|>b")]
    [InlineData("- a\n- <|>b", "- a\n\n  <|>b")]
    [InlineData("1. a\n2. <|>b\n3. c", "1. a\n\n   <|>b\n\n3. c")]
    public void BackspaceAtItemStart(string before, string after) => Assert.Equal(after, Run(before, "Backspace"));

    // Headless items: Enter on an empty one makes it an item again, Backspace deletes it into the
    // previous row; a non-empty one splits on Enter.
    [Theory]
    [InlineData("- a\n\n  <|>", "Enter", "- a\n\n- <|>")]
    [InlineData("- a\n\n  <|>", "Backspace", "- a<|>")]
    [InlineData("- a\n\n  b<|>c", "Enter", "- a\n\n  b\n\n  <|>c")]
    [InlineData("- a\n\n  <|>b", "Backspace", "- a<|>b")]
    public void HeadlessItemEnterAndBackspace(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    // Shift+Enter types a soft break; Ctrl+Enter adds a headless item below, splitting at the caret.
    [Theory]
    [InlineData("- a<|>b", "Shift+Enter", "- a\n  <|>b")]
    [InlineData("- a\n\n  b<|>c", "Shift+Enter", "- a\n\n  b\n  <|>c")]
    [InlineData("- a<|>b", "Ctrl+Enter", "- a\n\n  <|>b")]
    [InlineData("- ab<|>", "Ctrl+Enter", "- ab\n\n  <|>")]
    [InlineData("- a\n\n  <|>", "Ctrl+Enter", "- a\n\n  <empty>\n\n  <|>")]
    public void SoftBreakAndCtrlEnter(string before, string keys, string after) => Assert.Equal(after, Run(before, keys));

    [Theory]
    [InlineData("- a<|>\n- b", "- a<|>b")]
    [InlineData("- a<|>\n    - b\n        - c", "- a<|>b\n    - c")]
    [InlineData("- a<|>\n\nnext", "- a<|>next")]
    public void DeleteAtItemEndMergesNextRow(string before, string after) => Assert.Equal(after, Run(before, "Delete"));

    // Typora: list syntax typed at the start of an item starts a sub-list right there, and Enter at its
    // end goes on at the sub-list's level (Q48).
    [Fact]
    public void ListSyntaxAtItemStartStartsSubList()
    {
        var host = EditorTestHost.Load("- <|>sub directly");
        host.Type("- ");
        Assert.Equal("- - <|>sub directly", host.Dump());
        host.Press("End Enter");
        host.Type("sub appended");
        Assert.Equal("- - sub directly\n  - sub appended<|>", host.Dump());
        var reopened = TestMarkdown.Import(TestMarkdown.Serialize(host.State.Document));
        Assert.Equal(ModelDump.Structure(host.State.Document), ModelDump.Structure(reopened));
    }

    // The bare row of "- - a" (checked in Typora): Backspace at the start of the inner item gives its
    // content to the outer item; Tab cannot indent it and types a tab (escaped with a zero-width space);
    // Shift+Tab moves it out, the outer item disappearing with its empty row; Enter on an empty inner
    // item leaves one level.
    [Theory]
    [InlineData("- - <|>a", "Backspace", "- <|>a")]
    [InlineData("- - <|>a\n  - b", "Backspace", "- <|>a\n  - b")]
    [InlineData("- - <|>a", "Tab", "- - ​\t<|>a")]
    [InlineData("- - <|>a", "Shift+Tab", "- <|>a")]
    [InlineData("- - <|>", "Enter", "- <|>")]
    [InlineData("- b<|>\n- - a", "Delete", "- b<|>a")]
    public void BareRowKeys(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    // Typora: a rule can sit inside a list item (Q47), also made there with "---" and Enter.
    [Fact]
    public void RuleInsideListItem()
    {
        var document = TestMarkdown.RoundTrip("- a\n\n  ---\n\n  b\n- c\n");
        var item = (ListItem)TestMarkdown.Single<ListBlock>(document).Children[0];
        Assert.IsType<ThematicBreak>(item.Children[1]);

        var host = EditorTestHost.Load("- a<|>\n- c");
        host.Press("Ctrl+Enter").Type("---").Press("Enter");
        Assert.Equal("- a\n\n  ---\n\n  <|>\n\n- c", host.Dump());
        var edited = (ListItem)TestMarkdown.Single<ListBlock>(host.State.Document).Children[0];
        Assert.IsType<ThematicBreak>(edited.Children[1]);
    }

    [Fact]
    public void ItemsKeepTheirIdsWhenIndented()
    {
        var host = EditorTestHost.Load("- a\n- <#b>b<|>");
        var paragraph = host.Id("b");
        host.Press("Tab");
        var outer = TestMarkdown.Single<ListBlock>(host.State.Document);
        var nested = Assert.IsType<ListBlock>(((ListItem)outer.Children[0]).Children[1]);
        Assert.Equal(paragraph, ((ListItem)nested.Children[0]).Children[0].Id);
    }

    [Fact]
    public void UndoRestoresListExactly()
    {
        const string before = "- one\n    - nested one<|>\n        - nested square one\n    - nested two\n- two\n- three";
        var host = EditorTestHost.Load(before);
        host.Press("Shift+Tab Shift+Tab");
        host.Press("Ctrl+Z Ctrl+Z");
        Assert.Equal(before, host.Dump());
    }
}
