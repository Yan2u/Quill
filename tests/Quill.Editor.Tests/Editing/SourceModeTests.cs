using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Tests.Conformance;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// 06 and 11-source-mode.md: switching to the source mode and back. The caret follows the text; without
/// changes the document comes back as it was; changed text keeps the unchanged blocks (same objects);
/// each source edit is an undo item of the editing mode.
/// </summary>
public sealed class SourceModeTests
{
    private const string Sample = "# Title\n\nFirst paragraph.\n\n- one\n- two\n\n```cs\nvar x = 1;\n```\n\nLast paragraph.";

    /// <summary>The editing host and a source session started from it.</summary>
    private static (EditorTestHost Host, SourceSession Session, EditorSession Source) Enter(string dsl)
    {
        var host = EditorTestHost.Load(dsl);
        var session = host.Session.EnterSource();
        var source = new EditorSession(session.Source, host.Session.Services);
        return (host, session, source);
    }

    private static string SourceDump(EditorSession source)
    {
        string text = SourceText.ToText(source.State.Document);
        return text.Insert(SourceMode.OffsetOf(source.State.Document, source.State.Selection.Head), "<|>");
    }

    [Theory]
    [InlineData("# Ti<|>tle", "# Ti<|>tle")]
    [InlineData("a\n\n- b\n- c<|>d", "a\n\n- b\n- c<|>d")]
    [InlineData("> quo<|>ted", "> quo<|>ted")]
    [InlineData("```cs\nvar<|> x;\n```", "```cs\nvar<|> x;\n```")]
    [InlineData("| a | b |\n| - | - |\n| c | d<|> |", "| a | b |\n| - | - |\n| c | d<|> |")]
    [InlineData("**bo<|>ld** text", "**bo<|>ld** text")]
    public void TheCaretGoesToTheSameTextInTheSource(string dsl, string source)
    {
        var (_, _, session) = Enter(dsl);
        Assert.Equal(source, SourceDump(session));
    }

    [Fact]
    public void WithoutChangesTheDocumentComesBackAsItWas()
    {
        var (host, session, source) = Enter(Sample.Insert(Sample.IndexOf("First", StringComparison.Ordinal) + 3, "<|>"));
        var original = host.State.Document;
        source.Execute(new SetSelection(TextSelection.Caret(source.State.Document.Children[^1].Id, 2)));
        host.Session.LeaveSource(session, source);
        Assert.Same(original, host.State.Document);
        Assert.Equal(new TextPosition(original.Children[^1].Id, 2), host.State.Selection.Head);
        Assert.False(host.Session.History.CanUndo);
    }

    [Fact]
    public void AnEditKeepsTheUnchangedBlocksAndTheirIds()
    {
        var (host, session, source) = Enter(Sample.Insert(Sample.IndexOf("First", StringComparison.Ordinal) + 5, "<|>"));
        var before = host.State.Document.Children.ToList();
        foreach (char c in " edited")
        {
            source.Execute(new InsertText(c.ToString()));
        }

        host.Session.LeaveSource(session, source);
        var after = host.State.Document.Children.ToList();
        Assert.Equal(before.Count, after.Count);
        Assert.Same(before[0], after[0]);
        Assert.NotSame(before[1], after[1]);
        for (int i = 2; i < before.Count; i++)
        {
            Assert.Same(before[i], after[i]);
        }

        Assert.Equal("First edited paragraph.", ((Paragraph)after[1]).Text);
        Assert.Equal(Sample.Replace("First paragraph", "First edited paragraph", StringComparison.Ordinal), MarkdownSerializer.Serialize(host.State.Document).TrimEnd('\n'));
        Assert.Equal(new TextPosition(after[1].Id, 12), host.State.Selection.Head);
    }

    [Fact]
    public void StructureWrittenInTheSourceIsParsed()
    {
        var (host, session, source) = Enter("text<|>");
        source.Execute(new SplitBlock());
        source.Execute(new SplitBlock());
        foreach (char c in "## New heading")
        {
            source.Execute(new InsertText(c.ToString()));
        }

        host.Session.LeaveSource(session, source);
        Assert.IsType<Heading>(host.State.Document.Children[1]);
        Assert.Equal(new TextPosition(host.State.Document.Children[1].Id, 11), host.State.Selection.Head);
    }

    [Fact]
    public void EverySourceEditIsAnUndoItemOfTheEditingMode()
    {
        var (host, session, source) = Enter("one<|>\n\ntwo\n\nthree");
        var untouched = host.State.Document.Children[2];
        source.Execute(new InsertText("!"));
        var lineTwo = source.State.Document.Children[2];
        source.Execute(new SetSelection(TextSelection.Caret(lineTwo.Id, 3)));
        source.Execute(new InsertText("?"));
        Assert.Equal(2, source.History.UndoItems.Count);
        host.Session.LeaveSource(session, source);
        Assert.Equal("one!\n\ntwo?\n\nthree", Text(host));

        host.Press("Ctrl+Z");
        Assert.Equal("one!\n\ntwo\n\nthree", Text(host));
        host.Press("Ctrl+Z");
        Assert.Equal("one\n\ntwo\n\nthree", Text(host));
        Assert.Same(untouched, host.State.Document.Children[2]);
        Assert.Equal(new TextPosition(host.State.Document.Children[0].Id, 3), host.State.Selection.Head);
        host.Press("Ctrl+Y Ctrl+Y");
        Assert.Equal("one!\n\ntwo?\n\nthree", Text(host));
        Assert.Same(untouched, host.State.Document.Children[2]);
        Assert.Equal(new TextPosition(host.State.Document.Children[1].Id, 4), host.State.Selection.Head);
    }

    [Fact]
    public void TheModifiedStateFollowsSourceEdits()
    {
        var (host, session, source) = Enter("one<|>");
        source.Execute(new InsertText("!"));
        host.Session.LeaveSource(session, source);
        Assert.True(host.Session.IsModified);
        host.Press("Ctrl+Z");
        Assert.False(host.Session.IsModified);
    }

    /// <summary>On the specification examples, a change at the end keeps the blocks before it.</summary>
    [Fact]
    public void RebuildingKeepsThePrefixOnTheSpecCorpus()
    {
        int kept = 0, total = 0;
        foreach (var example in SpecCorpus.CommonMark.Concat(SpecCorpus.Gfm))
        {
            var ids = new SequentialBlockIdSource();
            var document = MarkdownImporter.Import(example.Markdown, ids).Document;
            if (document.Children.Count < 2)
            {
                continue;
            }

            string text = MarkdownSerializer.SerializeNormalized(document, out var starts);
            string edited = text + "\n\nappended";
            var rebuilt = SourceMode.Rebuild(document, text, starts, edited, ids, EditorOptions.Default);
            Assert.Equal(edited, MarkdownSerializer.SerializeNormalized(rebuilt));
            total++;
            kept += ReferenceEquals(rebuilt.Children[0], document.Children[0]) ? 1 : 0;
        }

        Assert.True(kept > total * 0.9, $"kept the first block in {kept} of {total}");
    }

    private static string Text(EditorTestHost host) => MarkdownSerializer.Serialize(host.State.Document).TrimEnd('\n');
}
