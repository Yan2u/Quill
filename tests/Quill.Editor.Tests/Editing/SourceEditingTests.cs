using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Input;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// 11-source-mode.md: editing a source document, one block per line. The text is written with
/// <c>&lt;|&gt;</c> for the caret (or <c>&lt;^&gt;</c> and <c>&lt;$&gt;</c> for a selection) at offsets of the joined text.
/// </summary>
public sealed class SourceEditingTests
{
    /// <summary>A session on a source document, driven by keys like the editor.</summary>
    internal sealed class SourceHost
    {
        public SourceHost(string marked)
        {
            Ids = new SequentialBlockIdSource();
            var (text, anchor, head) = Parse(marked);
            var document = SourceText.ToDocument(text, DocumentFormat.Default, Ids);
            Session = new EditorSession(
                new EditorState { Document = document, Selection = new TextSelection(PositionOf(document, anchor), PositionOf(document, head)) },
                new EditorServices { Ids = Ids, Clock = new ManualClock() });
        }

        public EditorSession Session { get; }

        public SequentialBlockIdSource Ids { get; }

        public ClipboardContent? Clipboard { get; private set; }

        public SourceHost Press(string chords)
        {
            foreach (string text in chords.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                switch (KeyMap.Default.Find(KeyChord.Parse(text)))
                {
                    case CommandBinding binding:
                        Session.Execute(binding.Command);
                        break;
                    case ClipboardBinding { Action: ClipboardAction.Copy }:
                        Clipboard = Session.Copy();
                        break;
                    case ClipboardBinding { Action: ClipboardAction.Paste } when Clipboard is not null:
                        Session.Execute(new Paste(Clipboard));
                        break;
                    case ClipboardBinding { Action: ClipboardAction.Cut }:
                        Clipboard = Session.Copy();
                        Session.Execute(new Cut());
                        break;
                }
            }

            return this;
        }

        public SourceHost Type(string text)
        {
            foreach (char c in text)
            {
                Session.Execute(new InsertText(c.ToString()));
            }

            return this;
        }

        public SourceHost Execute(Command command)
        {
            Session.Execute(command);
            return this;
        }

        /// <summary>The text with the caret or selection marked.</summary>
        public string Dump()
        {
            var document = Session.State.Document;
            Assert.All(document.Children, block => Assert.IsType<SourceLine>(block));
            string text = SourceText.ToText(document);
            int anchor = OffsetOf(document, Session.State.Selection.Anchor);
            int head = OffsetOf(document, Session.State.Selection.Head);
            if (anchor == head)
            {
                return text.Insert(head, "<|>");
            }

            var (start, end) = anchor < head ? (anchor, head) : (head, anchor);
            return text.Insert(end, "<$>").Insert(start, "<^>");
        }

        private static (string Text, int Anchor, int Head) Parse(string marked)
        {
            int caret = marked.IndexOf("<|>", StringComparison.Ordinal);
            if (caret >= 0)
            {
                return (marked.Remove(caret, 3), caret, caret);
            }

            int start = marked.IndexOf("<^>", StringComparison.Ordinal);
            string text = marked.Remove(start, 3);
            int end = text.IndexOf("<$>", StringComparison.Ordinal);
            return (text.Remove(end, 3), start, end);
        }

        private static TextPosition PositionOf(Document document, int offset)
        {
            foreach (LeafBlock line in document.Children)
            {
                if (offset <= line.Text.Length)
                {
                    return new TextPosition(line.Id, offset);
                }

                offset -= line.Text.Length + 1;
            }

            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        private static int OffsetOf(Document document, Position position)
        {
            var at = (TextPosition)position;
            int offset = 0;
            foreach (LeafBlock line in document.Children)
            {
                if (line.Id == at.Block)
                {
                    return offset + at.Offset;
                }

                offset += line.Text.Length + 1;
            }

            throw new InvalidOperationException("The caret is not in the document.");
        }
    }

    private static string Press(string before, string keys) => new SourceHost(before).Press(keys).Dump();

    [Fact]
    public void TextBecomesOneLinePerBlockAndBack()
    {
        string text = "# Title\n\n- a\n  - b\n\n```cs\nvar x;\n```\n";
        var document = SourceText.ToDocument(text, DocumentFormat.Default, new SequentialBlockIdSource());
        Assert.Equal(9, document.Children.Count);
        Assert.Equal(text, SourceText.ToText(document));
    }

    [Theory]
    [InlineData("ab<|>cd", "Enter", "ab\n<|>cd")]
    [InlineData("  ab<|>cd", "Enter", "  ab\n  <|>cd")]
    [InlineData("ab<|>", "Shift+Enter", "ab\n<|>")]
    [InlineData("a<^>b\nc<$>d", "Enter", "a\n<|>d")]
    [InlineData("ab\n<|>cd", "Backspace", "ab<|>cd")]
    [InlineData("ab<|>\ncd", "Delete", "ab<|>cd")]
    [InlineData("<|>ab", "Backspace", "<|>ab")]
    [InlineData("a<^>b\ncd\ne<$>f", "Backspace", "a<|>f")]
    [InlineData("a<|>b", "Tab", "a\t<|>b")]
    [InlineData("    a<|>b", "Shift+Tab", "a<|>b")]
    [InlineData("\ta<|>b", "Shift+Tab", "a<|>b")]
    public void KeysEditLines(string before, string keys, string after) => Assert.Equal(after, Press(before, keys));

    [Theory]
    [InlineData("a<|>b", "Ctrl+B Ctrl+Shift+K Ctrl+Shift+Q Ctrl+1 Ctrl+Shift+]", "a<|>b")]
    [InlineData("- [ ] a<|>", "Enter", "- [ ] a\n<|>")]
    [InlineData("<|>", "#", "#<|>")]
    public void NoInputRulesOrFormatsInSource(string before, string keys, string after)
    {
        var host = new SourceHost(before);
        if (keys == "#")
        {
            host.Type("# x").Press("Enter");
            Assert.Equal("# x\n<|>", host.Dump());
            return;
        }

        Assert.Equal(after, host.Press(keys).Dump());
    }

    [Fact]
    public void PasteSplitsLinesAndCopyJoinsThem()
    {
        var host = new SourceHost("x<^>a\nb<$>y");
        host.Press("Ctrl+C");
        Assert.Equal("a\nb", host.Clipboard!.PlainText);
        Assert.Equal("a\nb", host.Clipboard.Markdown);
        host.Press("Right Ctrl+V");
        Assert.Equal("xa\nba\nb<|>y", host.Dump());
    }

    [Fact]
    public void PastedMarkdownIsNotParsed()
    {
        var host = new SourceHost("<|>");
        host.Execute(new Paste(new ClipboardContent("# h\n\n- a", "h a")));
        Assert.Equal("# h\n\n- a<|>", host.Dump());
    }

    [Fact]
    public void UndoRestoresTheLines()
    {
        var host = new SourceHost("ab<|>cd");
        host.Press("Enter").Type("x").Press("Ctrl+Z Ctrl+Z");
        Assert.Equal("ab<|>cd", host.Dump());
    }
}
