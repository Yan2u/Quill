using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// M5.6: the modified state shown in the title bar and the preparation of a save (06: the paragraph at
/// the caret is committed first, the snapshot is written as is).
/// </summary>
public sealed class SaveTests
{
    [Fact]
    public void LoadedDocumentIsNotModified()
    {
        var host = EditorTestHost.Load("a<|>");
        Assert.False(host.Session.IsModified);
        host.Press("Left Shift+Right");
        Assert.False(host.Session.IsModified);
    }

    [Fact]
    public void EditsModifyAndUndoingThemAllRestoresTheLoadedState()
    {
        var host = EditorTestHost.Load("a<|>");
        host.Type("bc");
        Assert.True(host.Session.IsModified);
        host.Press("Ctrl+Z");
        Assert.False(host.Session.IsModified);
        host.Press("Ctrl+Y");
        Assert.True(host.Session.IsModified);
    }

    [Fact]
    public void SavingClearsTheMarkUntilTheNextEdit()
    {
        var host = EditorTestHost.Load("a<|>");
        host.Type("b");
        host.Session.MarkSaved(host.Session.PrepareSave());
        Assert.False(host.Session.IsModified);

        // Typing right after the save does not merge into the saved item: undoing it is the saved document.
        host.Type("c");
        Assert.True(host.Session.IsModified);
        host.Press("Ctrl+Z");
        Assert.Equal("ab<|>", host.Dump());
        Assert.False(host.Session.IsModified);

        // Undoing past the save point is a change as well.
        host.Press("Ctrl+Z");
        Assert.True(host.Session.IsModified);
        host.Press("Ctrl+Y");
        Assert.False(host.Session.IsModified);
    }

    [Fact]
    public void AnEditAfterUndoingPastTheSaveCannotReturnToIt()
    {
        var host = EditorTestHost.Load("a<|>");
        host.Type("b");
        host.Session.MarkSaved(host.Session.PrepareSave());
        host.Press("Ctrl+Z").Type("x");
        Assert.True(host.Session.IsModified);
        Assert.False(host.Session.History.CanRedo);
    }

    [Fact]
    public void ASnapshotTakenBeforeLaterEditsMarksOnlyItsOwnContent()
    {
        // A background save completes after the user typed on: the document is still modified.
        var host = EditorTestHost.Load("a<|>");
        host.Type("b");
        var snapshot = host.Session.PrepareSave();
        host.Advance(TimeSpan.FromSeconds(2)).Type("c");
        host.Session.MarkSaved(snapshot);
        Assert.True(host.Session.IsModified);
        host.Press("Ctrl+Z");
        Assert.False(host.Session.IsModified);
    }

    [Fact]
    public void PrepareSaveCommitsTheParagraphAtTheCaret()
    {
        var host = EditorTestHost.Load("<|>");
        host.Type("Title").Press("Shift+Enter").Type("===");
        Assert.IsType<Paragraph>(host.State.Document.Children[0]);
        var snapshot = host.Session.PrepareSave();
        Assert.IsType<Heading>(snapshot.Document.Children[0]);
        Assert.Same(host.State.Document, snapshot.Document);
        Assert.Equal("Title\n===", MarkdownSerializer.Serialize(snapshot.Document).TrimEnd('\n'));
    }

    [Theory]
    [InlineData("", "x")]
    [InlineData("a\n", "xa\n")]
    [InlineData("---\n\nb", "x\n\n---\n\nb")]
    public void AnOpenedFileStartsWithTheCaretAtItsStart(string markdown, string expected)
    {
        var ids = new SequentialBlockIdSource();
        var session = new EditorSession(EditorState.AtStart(MarkdownImporter.Import(markdown, ids).Document), new EditorServices { Ids = ids, Clock = new ManualClock() });
        session.Execute(new InsertText("x"));
        Assert.Equal(expected, MarkdownSerializer.Serialize(session.PrepareSave().Document));
    }

    [Fact]
    public void EditsKeepTheFileFormat()
    {
        var ids = new SequentialBlockIdSource();
        var document = MarkdownImporter.ImportFile([0xEF, 0xBB, 0xBF, (byte)'a', (byte)'\r', (byte)'\n'], ids).Document;
        var session = new EditorSession(
            new EditorState { Document = document, Selection = TextSelection.Caret(document.Children[0].Id, 1) },
            new EditorServices { Ids = ids, Clock = new ManualClock() });
        session.Execute(new InsertText("b"));
        session.Execute(new SplitBlock());
        session.Execute(new InsertText("c"));
        var bytes = MarkdownSerializer.SerializeFile(session.PrepareSave().Document);
        Assert.Equal([0xEF, 0xBB, 0xBF, .. "ab\r\n\r\nc\r\n"u8.ToArray()], bytes);
    }
}
