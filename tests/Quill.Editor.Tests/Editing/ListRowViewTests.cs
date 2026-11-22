using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Editing.Lists;
using Quill.Editor.Tests.Support;
using Quill.Editor.Transactions;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// The list row view (02-document-model.md) and the numbering rules (Q26, Q32), as required by
/// 08-testing-and-debugging.md: rows to tree and back, numbering by source pattern for added and
/// removed items, single-item lists.
/// </summary>
public sealed class ListRowViewTests
{
    [Theory]
    [InlineData("- a\n- b")]
    [InlineData("- a\n    - b\n        - c\n    - d\n- e")]
    [InlineData("1. a\n\n   b\n\n   - c\n\n   d\n2. e")]
    [InlineData("- [x] a\n- [ ] b\n    1) c\n    2) d")]
    [InlineData("> - a\n>   - b")]
    [InlineData("- - a\n  - b\n- c")]
    [InlineData("- a\n\n  ```\n  code\n  ```\n- b")]
    public void WritingBackUnchangedRowsKeepsTheList(string markdown)
    {
        var document = TestMarkdown.RoundTrip(markdown + "\n");
        var list = Navigation.Leaves(document).First();
        var view = ListRowView.ForBlock(document, list.Id)!;
        var builder = new TransactionBuilder(new State.EditorState { Document = document, Selection = State.TextSelection.Caret(list.Id, 0) }, new SequentialBlockIdSource());
        view.WriteBack(builder);
        Assert.Equal(markdown + "\n", TestMarkdown.Serialize(builder.Document));
        Assert.Equal(ModelDump.Structure(document), ModelDump.Structure(builder.Document));
        Assert.Equal(All(document).Select(static block => block.Id), All(builder.Document).Select(static block => block.Id));
    }

    [Fact]
    public void RowsHaveKindsAndLevels()
    {
        var document = TestMarkdown.RoundTrip("- a\n\n  b\n\n  - c\n\n  d\n- e\n");
        var view = ListRowView.ForBlock(document, Navigation.Leaves(document).First().Id)!;
        Assert.Equal(
            ["a Item 1", "b Content 1", "c Item 2", "d Content 1", "e Item 1"],
            view.Rows.Select(static row => $"{((LeafBlock)row.Block!).Text} {row.Kind} {row.Level}"));
    }

    // "- - a" (Q48): the outer item's row is its marker alone, a bare row before the sub-list's rows.
    [Fact]
    public void ItemStartingWithSubListHasBareRow()
    {
        var document = TestMarkdown.RoundTrip("- - a\n  - b\n- c\n");
        var view = ListRowView.ForBlock(document, Navigation.Leaves(document).First().Id)!;
        Assert.Equal(
            ["(bare) Item 1", "a Item 2", "b Item 2", "c Item 1"],
            view.Rows.Select(static row => $"{(row.Block is LeafBlock leaf ? leaf.Text : "(bare)")} {row.Kind} {row.Level}"));
    }

    // Adding an item: "increasing" and "other" lists are renumbered from the first number; "same"
    // lists give the new item the same number. A single item counts as increasing when it is 1.
    [Theory]
    [InlineData("1. a<|>\n2. b\n3. c", "1. a\n2. <|>\n3. b\n4. c")]
    [InlineData("3. a<|>\n4. b", "3. a\n4. <|>\n5. b")]
    [InlineData("1. a<|>\n1. b\n1. c", "1. a\n1. <|>\n1. b\n1. c")]
    [InlineData("2. a<|>\n7. b\n3. c", "2. a\n3. <|>\n4. b\n5. c")]
    [InlineData("1. a<|>", "1. a\n2. <|>")]
    [InlineData("5. a<|>", "5. a\n5. <|>")]
    public void AddingItemFollowsNumbering(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Enter");
        Assert.Equal(after, host.Dump());
    }

    // Removing an item (here: it becomes headless, or leaves the list) leaves the written numbers alone.
    [Theory]
    [InlineData("1. a\n2. <|>b\n3. c", "Backspace", "1. a\n\n   <|>b\n\n3. c")]
    [InlineData("1. a\n2. <|>b\n3. c", "Shift+Tab", "1. a\n\n<|>b\n\n1. c")]
    [InlineData("1. a\n1. <|>b\n1. c", "Backspace", "1. a\n\n   <|>b\n\n1. c")]
    [InlineData("1. a\n2. b\n3. <|>", "Enter", "1. a\n2. b\n\n<|>")]
    public void RemovingItemKeepsNumbers(string before, string keys, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press(keys);
        Assert.Equal(after, host.Dump());
    }

    // Indenting moves an item into a list: that list gains an item.
    [Theory]
    [InlineData("1. a\n    1. b\n2. <|>c", "1. a\n    1. b\n    2. <|>c")]
    [InlineData("1. a\n    1. b\n    1. c\n2. <|>d", "1. a\n    1. b\n    1. c\n    1. <|>d")]
    public void IndentedItemJoinsListNumbering(string before, string after)
    {
        var host = EditorTestHost.Load(before);
        host.Press("Tab");
        Assert.Equal(after, host.Dump());
    }

    // Merging away the paragraph between two ordered lists makes them adjacent: with the setting on
    // the second is written with the other delimiter and stays a list of its own on reopening (Q25);
    // with it off the pair is reported for the view's hint.
    [Theory]
    [InlineData(true, "1. a<|>para\n\n1) x", 2)]
    [InlineData(false, "1. a<|>para\n\n1. x", 1)]
    public void AdjacentListsAfterEdit(bool switchMarker, string after, int listsOnReopening)
    {
        var options = EditorOptions.Default with { SwitchMarkerForAdjacentLists = switchMarker };
        var host = EditorTestHost.Load("1. a\n\n<|>para\n\n1. x", options);
        host.Press("Backspace");
        Assert.Equal(after, host.Dump());
        Assert.Single(ListRules.FindMergingAdjacentLists(host.State.Document));
        var reopened = TestMarkdown.Import(TestMarkdown.Serialize(host.State.Document, options));
        Assert.Equal(listsOnReopening, reopened.Children.Count(static child => child is ListBlock));
    }

    private static IEnumerable<Block> All(Block block)
    {
        yield return block;
        if (block is ContainerBlock container)
        {
            foreach (var inner in container.Children.SelectMany(All))
            {
                yield return inner;
            }
        }
    }
}
