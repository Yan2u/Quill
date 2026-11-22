using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Input;

/// <summary>
/// M5.4, layer 1 (07, 08): input method event sequences played against <see cref="ITextInputClient"/>
/// as Microsoft Pinyin sends them (Q6): typing, a composition committed as one undo item, Esc
/// (the text emptied, no cancel flag), compositions over a selection and in a virtual paragraph,
/// updates outside a composition, and what the service reads back.
/// </summary>
public sealed class TextInputClientTests
{
    [Fact]
    public void TheServiceSeesTheActiveLeafOnly()
    {
        var host = EditorTestHost.Load("first\n\nse<^>co<$>nd");
        var client = new EditorTextInput(host.Session);
        Assert.Equal("second", client.ActiveText);
        Assert.Equal((2, 4), client.Selection);
        long revision = client.Revision;
        host.Press("Up");
        Assert.Equal("first", client.ActiveText);
        Assert.NotEqual(revision, client.Revision);
    }

    [Fact]
    public void AVirtualParagraphShowsEmptyText()
    {
        var host = EditorTestHost.Load("[toc]\n\n<gap>\n\n[toc]");
        var client = new EditorTextInput(host.Session);
        Assert.Null(client.ActiveBlock);
        Assert.Equal(string.Empty, client.ActiveText);
        Assert.Equal((0, 0), client.Selection);
    }

    [Fact]
    public void ACommittedCompositionIsOneUndoItem()
    {
        var host = EditorTestHost.Load("ab<|>");
        var client = new EditorTextInput(host.Session);
        Compose(client, 2, "n", "ni", "你");
        Assert.True(client.IsComposing);
        Assert.Equal("ab你", client.ActiveText);
        client.EndComposition();
        Assert.False(client.IsComposing);
        Assert.Equal("ab你<|>", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("ab<|>", host.Dump());
    }

    [Fact]
    public void TheCompositionIsMarkedForTheProjection()
    {
        var host = EditorTestHost.Load("ab<|>");
        var client = new EditorTextInput(host.Session);
        Compose(client, 2, "zh", "zhong");
        var composition = host.State.Composition!;
        Assert.Equal((2, 7), (composition.Start, composition.Start + composition.Length));
        client.EndComposition();
    }

    [Fact]
    public void EscEmptiesTheCompositionAndLeavesNothing()
    {
        // Microsoft Pinyin: the text is replaced by nothing, then the composition ends without a cancel flag.
        var host = EditorTestHost.Load("ab<|>");
        var client = new EditorTextInput(host.Session);
        Compose(client, 2, "t", "tu", "tui");
        client.ReplaceText(2, 5, string.Empty, 2, 2);
        client.EndComposition();
        Assert.Equal("ab<|>", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("ab<|>", host.Dump());
    }

    [Fact]
    public void ACompositionReplacesTheSelectionAndEscRestoresIt()
    {
        var host = EditorTestHost.Load("x<^>abc<$>x");
        var client = new EditorTextInput(host.Session);
        client.BeginComposition();
        Assert.Equal("xx", client.ActiveText);
        client.ReplaceText(1, 1, "z", 2, 2);
        client.ReplaceText(1, 2, "字", 2, 2);
        client.EndComposition();
        Assert.Equal("x字<|>x", host.Dump());

        host = EditorTestHost.Load("x<^>abc<$>x");
        client = new EditorTextInput(host.Session);
        client.BeginComposition();
        client.ReplaceText(1, 1, "z", 2, 2);
        client.ReplaceText(1, 2, string.Empty, 1, 1);
        client.EndComposition();
        Assert.Equal("x<^>abc<$>x", host.Dump());
    }

    [Fact]
    public void ACompositionInAVirtualParagraphMakesAParagraphOrNothing()
    {
        var host = EditorTestHost.Load("[toc]\n\n<gap>\n\n[toc]");
        var client = new EditorTextInput(host.Session);
        Compose(client, 0, "zi", "字");
        client.EndComposition();
        Assert.Equal("[toc]\n\n字<|>\n\n[toc]", host.Dump());
        host.Press("Ctrl+Z");
        Assert.Equal("[toc]\n\n<gap>\n\n[toc]", host.Dump());

        Compose(client, 0, "zi");
        client.ReplaceText(0, 2, string.Empty, 0, 0);
        client.EndComposition();
        Assert.Equal("[toc]\n\n<gap>\n\n[toc]", host.Dump());
    }

    [Fact]
    public void InputRulesWaitUntilTheCompositionEnds()
    {
        // A list marker composed by the input method converts only as typed text would, not mid-composition (03).
        var host = EditorTestHost.Load("<|>");
        var client = new EditorTextInput(host.Session);
        Compose(client, 0, "-", "- ");
        Assert.Equal("- ", client.ActiveText);
        client.EndComposition();
        Assert.False(client.IsComposing);
    }

    [Fact]
    public void UpdatesOutsideACompositionAreEdits()
    {
        var host = EditorTestHost.Load("ab<|>");
        var client = new EditorTextInput(host.Session);
        client.ReplaceText(2, 2, "c", 3, 3);
        Assert.Equal("abc<|>", host.Dump());
        client.ReplaceText(1, 3, string.Empty, 1, 1);
        Assert.Equal("a<|>", host.Dump());
        client.ReplaceText(1, 1, "\r", 2, 2);
        Assert.Equal("a\n\n<|>", host.Dump());
        client.InsertText("z");
        Assert.Equal("a\n\nz<|>", host.Dump());
    }

    [Fact]
    public void TheServiceMovesTheSelectionInsideTheLeaf()
    {
        var host = EditorTestHost.Load("hello<|>");
        var client = new EditorTextInput(host.Session);
        client.SetSelection(1, 3);
        Assert.Equal("h<^>el<$>lo", host.Dump());
    }

    /// <summary>A composition from <paramref name="start"/>: each text replaces the previous one, caret at its end.</summary>
    private static void Compose(EditorTextInput client, int start, params string[] steps)
    {
        client.BeginComposition();
        int length = 0;
        foreach (string text in steps)
        {
            client.ReplaceText(start, start + length, text, start + text.Length, start + text.Length);
            length = text.Length;
        }
    }
}
