using System.Text;
using System.Text.RegularExpressions;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.Editing;
using Quill.Editor.Markdown;
using Quill.Editor.State;
using Quill.Editor.Tests.Support;

namespace Quill.Editor.Tests.Editing;

/// <summary>
/// Property tests of 08-testing-and-debugging.md: random command sequences with fixed seeds. After
/// every step the document satisfies the invariants of 02 and the selection is valid; undoing
/// everything returns to the initial document and redoing everything to the final one; the committed
/// final document survives <c>Import(Serialize(d))</c> with the same structure, except for shapes that
/// Markdown cannot express (<see cref="Unrepresentable"/>).
/// </summary>
public sealed partial class EditingPropertyTests
{
    private const int Steps = 150;

    private static readonly Lock StatsLock = new();

    private static readonly string[] Starts =
    [
        "<|>",
        "# Title<|>\n\nSome *text* here.\n\n- one\n- two\n    - nested\n\n> quote\n>\n> more\n\n```js\ncode\nmore\n```\n\n| a | b |\n| - | - |\n| 1 | 2 |\n\n[^1]: note\n\n[x]: http://x \"t\"\n\n***\n\n1. a\n2. b",
        "- a<|>\n- b\n\n  headless\n- c\n    1. x\n    2. y\n- [ ] task",
        "> > deep<|>\n> back\n\npara\n\n$$\nx^2\n$$\n\n[toc]\n\nend",
        "1. one\n1. one again<|>\n\n   - inner\n\ntext\n\n- [x] done\n- [ ] todo",
    ];

    private static readonly string[] Keys =
    [
        "Enter", "Enter", "Shift+Enter", "Ctrl+Enter", "Tab", "Tab", "Shift+Tab", "Shift+Tab",
        "Backspace", "Backspace", "Backspace", "Delete", "Delete", "Ctrl+Backspace",
        "Left", "Right", "Up", "Down", "Home", "End", "Ctrl+Left", "Ctrl+Right",
        "Shift+Left", "Shift+Right", "Shift+Up", "Shift+Down", "Ctrl+Home", "Ctrl+End",
        "Ctrl+A", "Ctrl+2", "Ctrl+0", "Ctrl+C", "Ctrl+X", "Ctrl+V",
        "Ctrl+B", "Ctrl+I", "Ctrl+Shift+`", "Ctrl+=", "Ctrl+-", "Ctrl+Shift+K", "Ctrl+Shift+M", "Ctrl+Shift+Q",
        "Ctrl+Shift+[", "Ctrl+Shift+]", "Ctrl+Shift+X", "Ctrl+[", "Ctrl+]", "Ctrl+D", "Ctrl+Shift+D", "Ctrl+E", "Ctrl+L",
    ];

    private static readonly string[] Texts =
    [
        "a", "b", "x", " ", " ", "# ", "- ", "1. ", "> ", "[ ] ", "|", "\t", "    ", "*", "中文", "👍🏽", "é", "---", "```", "$$",
    ];

    private static readonly string[] Pastes =
    [
        "pasted", "two\n\nparagraphs", "- p\n- q", "# h\n\nt", "> q\n\nr", "```\nc\n```", "| a |\n| - |\n| b |", "1. n\n    - m",
    ];

    /// <summary>Seeds 1 to 24, or the range in QUILL_PROPERTY_SEEDS (such as "1-500") for a longer local run.</summary>
    public static TheoryData<int> Seeds
    {
        get
        {
            string? range = Environment.GetEnvironmentVariable("QUILL_PROPERTY_SEEDS");
            if (range?.Split('-') is [var from, var to] && int.TryParse(from, out int first) && int.TryParse(to, out int last))
            {
                return [.. Enumerable.Range(first, last - first + 1)];
            }

            return [.. Enumerable.Range(1, 24)];
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void RandomCommandsKeepInvariantsAndUndoRedoExactly(int seed)
    {
        var random = new Random(seed);
        string start = Starts[seed % Starts.Length];
        var host = EditorTestHost.Load(start);
        var initial = Snapshot(host.State.Document);
        var log = new StringBuilder();
        // QUILL_PROPERTY_TRACE=1 adds the states before the last steps to a failure message.
        bool trace = Environment.GetEnvironmentVariable("QUILL_PROPERTY_TRACE") == "1";
        var recent = new Queue<string>();
        for (int step = 0; step < Steps; step++)
        {
            if (trace)
            {
                recent.Enqueue($"--- before step {step} ---\n{StateDsl.Dump(host.State)}\n{ModelDump.Structure(host.State.Document)}");
                if (recent.Count > 3)
                {
                    recent.Dequeue();
                }
            }

            string action = Act(host, random);
            log.Append(action).Append(" | ");
            host.Advance(TimeSpan.FromMilliseconds(random.Next(0, 1500)));
            Check(host.State, $"seed {seed}, step {step} after {log}" + (trace ? "\n" + string.Join("\n", recent) : string.Empty));
        }

        host.Execute(new CommitParagraph());
        Check(host.State, $"seed {seed} after commit");
        var final = Snapshot(host.State.Document);
        RoundTrip(host.State.Document, $"seed {seed}: {log}");

        int undone = 0;
        while (host.Session.History.CanUndo)
        {
            host.Press("Ctrl+Z");
            Check(host.State, $"seed {seed}, undo {undone++}");
        }

        Assert.Equal(initial, Snapshot(host.State.Document));
        while (host.Session.History.CanRedo)
        {
            host.Press("Ctrl+Y");
            Check(host.State, $"seed {seed}, redo");
        }

        Assert.Equal(final, Snapshot(host.State.Document));
    }

    private static string Act(EditorTestHost host, Random random)
    {
        switch (random.Next(10))
        {
            case < 4:
                string text = Texts[random.Next(Texts.Length)];
                host.Type(text);
                return $"type '{text}'";
            case < 8:
                string key = Keys[random.Next(Keys.Length)];
                host.Clipboard ??= new ClipboardContent(null, Pastes[random.Next(Pastes.Length)]);
                host.Press(key);
                return key;
            case 8:
                string paste = Pastes[random.Next(Pastes.Length)];
                host.Execute(new Paste(new ClipboardContent(null, paste)));
                return $"paste '{paste}'";
            default:
                var selection = RandomSelection(host.State.Document, random);
                host.Execute(new SetSelection(selection));
                return $"select {selection}";
        }
    }

    private static TextSelection RandomSelection(Document document, Random random)
    {
        var leaves = Leaves(document).ToList();
        Position Pick()
        {
            var leaf = leaves[random.Next(leaves.Count)];
            return new TextPosition(leaf.Id, random.Next(leaf.Text.Length + 1));
        }

        var head = Pick();
        return random.Next(3) == 0 ? new TextSelection(Pick(), head) : TextSelection.Caret(head);
    }

    /// <summary>The invariants of 02 and a valid selection; the block index agrees with the tree.</summary>
    private static void Check(EditorState state, string context)
    {
        var document = state.Document;
        var errors = DocumentValidator.Validate(document).ToList();
        foreach (var block in All(document))
        {
            if (!ReferenceEquals(DocumentTree.Find(document, block.Id), block))
            {
                errors.Add($"The index finds another node for {block.Id}.");
            }
        }

        foreach (var position in new[] { state.Selection.Anchor, state.Selection.Head })
        {
            errors.AddRange(PositionErrors(document, position));
        }

        if (errors.Count > 0)
        {
            string markdown;
            try
            {
                markdown = MarkdownSerializer.SerializeNormalized(document);
            }
            catch (Exception exception)
            {
                markdown = $"(not serializable: {exception.Message})\n{ModelDump.Detailed(document)}";
            }

            Assert.Fail($"{context}: {string.Join("; ", errors)}\n{markdown}");
        }
    }

    private static IEnumerable<string> PositionErrors(Document document, Position position)
    {
        switch (position)
        {
            case TextPosition text:
                if (DocumentTree.TryFind(document, text.Block) is not LeafBlock leaf)
                {
                    yield return $"{text} is not in a leaf.";
                    yield break;
                }

                if (text.Offset < 0 || text.Offset > leaf.Text.Length || Text.TextBoundaries.SnapToGrapheme(leaf.Text, text.Offset) != text.Offset)
                {
                    yield return $"{text} is not a grapheme boundary of '{leaf.Text}'.";
                }

                break;
            case BoundaryPosition boundary:
                if (DocumentTree.TryFind(document, boundary.Parent) is not ContainerBlock container || boundary.Index < 0 || boundary.Index > container.Children.Count)
                {
                    yield return $"{boundary} is not a boundary.";
                }

                break;
        }
    }

    /// <summary>Import(Serialize(d)) has the structure of d, unless d holds a shape Markdown cannot express.</summary>
    private static void RoundTrip(Document document, string context)
    {
        // QUILL_PROPERTY_STATS=<file> records which documents were compared and why others were not.
        string? reason = Unrepresentable(document);
        if (Environment.GetEnvironmentVariable("QUILL_PROPERTY_STATS") is { Length: > 0 } stats)
        {
            lock (StatsLock)
            {
                File.AppendAllText(stats, (reason ?? "compared") + "\n");
            }
        }

        if (reason is not null)
        {
            return;
        }

        string markdown = MarkdownSerializer.Serialize(document);
        var reopened = MarkdownImporter.Import(markdown, new SequentialBlockIdSource()).Document;
        Assert.True(
            Normalized(document) == Normalized(reopened),
            $"{context}\n--- markdown ---\n{markdown}\n--- before ---\n{Normalized(document)}\n--- after ---\n{Normalized(reopened)}"
            + (Environment.GetEnvironmentVariable("QUILL_PROPERTY_TRACE") == "1" ? $"\n--- model ---\n{ModelDump.Detailed(document)}" : string.Empty));
    }

    /// <summary>
    /// Shapes the editor can hold for a while but Markdown cannot write as they are: an empty link
    /// label (written with a placeholder, Q55), definition slots that no longer form valid syntax, a
    /// footnote definition inside a container (read only at document level), item text or nested
    /// markers that make a marker line a rule, and paragraphs whose text holds block syntax not
    /// committed yet (a paragraph re-parses as itself once committed).
    /// </summary>
    private static string? Unrepresentable(Document document)
    {
        foreach (var block in All(document))
        {
            switch (block)
            {
                case FootnoteDefinition footnote when footnote.Name.Text.Any(c => char.IsWhiteSpace(c) || c is '[' or ']'):
                    return "footnote name";
                case ContainerBlock container and not Document when container.Children.Any(static child => child is FootnoteDefinition):
                    return "footnote definition inside a container (read only at document level)";
                case CodeFence code when code.Info != code.Info.Trim(' ', '\t'):
                    return "code language with surrounding spaces (an info string is trimmed, Q91)";
                case LinkReferenceDefinition link when link.Label.Text.Trim().Length == 0:
                    return "empty link label (written with a placeholder)";
                case LinkReferenceDefinition link when link.Destination.Text.Any(c => c is '\n' or '<' or '>')
                    || link.Label.Text.Any(c => c is '[' or ']'):
                    return "link definition slots";
                case Paragraph { IsEmpty: false } paragraph when !ReparsesAsItself(paragraph):
                    return "uncommitted block syntax";
                case ListItem { Task: TaskState.None, Children: [Paragraph first, ..] } when TaskMarker().IsMatch(first.Text.TrimStart(' ', '\t')):
                    return "task marker not converted in an item's first paragraph";
                case ListItem or BlockQuote when ((ContainerBlock)block).Children.OfType<Paragraph>().Any(InterruptibleContinuation):
                    return "indented continuation line with block syntax in a container";
                case ListBlock { IsOrdered: false } list when list.Children.Any(child => RuleLine().IsMatch(MarkerLine(list.MarkerChar, (ListItem)child))):
                    return "item text that makes its marker line a rule";
                case ContainerBlock container when container.Children.Zip(container.Children.Skip(1)).Any(static pair =>
                    pair.First is ListBlock && pair.Second is HtmlBlock html && html.Text.StartsWith(' ')):
                    return "indented HTML right after a list (it would join the last item)";
            }
        }

        return null;
    }

    private static bool ReparsesAsItself(Paragraph paragraph) =>
        MarkdownImporter.ImportFragment(paragraph.Text, new SequentialBlockIdSource()) is [Paragraph single] && single.Text == paragraph.Text;

    /// <summary>
    /// The first line of a bullet item as written: its marker, then the first line of its text or, for
    /// an item opening with a sub-list, that sub-list's marker line ("- - -" for two such levels over an
    /// empty item).
    /// </summary>
    private static string MarkerLine(char marker, ListItem item) => item.Children switch
    {
        [Paragraph first, ..] => marker + " " + first.Text.Split('\n')[0],
        [ListBlock { IsOrdered: false, Children: [ListItem inner, ..] } list, ..] => marker + " " + MarkerLine(list.MarkerChar, inner),
        _ => marker + " x",
    };

    /// <summary>
    /// A continuation line indented by whitespace whose rest would interrupt a paragraph: inside a
    /// container part of that indentation goes to the container's prefix, so the rest may start a block.
    /// </summary>
    private static bool InterruptibleContinuation(Paragraph paragraph) =>
        paragraph.Text.Split('\n').Skip(1).Any(static line => line.Length > 0 && char.IsWhiteSpace(line[0])
            && MarkdownImporter.ImportFragment("x\n" + line.TrimStart(), new SequentialBlockIdSource()) is not [Paragraph]);

    /// <summary>
    /// The structure dump with what Markdown normalizes: cell, heading and link label text trimmed, and
    /// leading whitespace of paragraphs and footnote text dropped (an empty paragraph's whitespace
    /// does not show).
    /// </summary>
    private static string Normalized(Document document) => ModelDump.Structure(Trim(document));

    private static Block Trim(Block block) => block switch
    {
        Paragraph { IsEmpty: true } paragraph => paragraph with { Text = string.Empty },
        Paragraph { IsEmpty: false } paragraph => paragraph with { Text = paragraph.Text.TrimStart(' ', '\t') },
        TableCell cell => cell with { Text = cell.Text.Trim(' ', '\t') },
        Heading heading => heading with { Text = heading.Text.Trim(' ', '\t') },
        DefinitionSlot { SlotKind: DefinitionSlotKind.LinkLabel } label => label with { Text = label.Text.Trim(' ', '\t') },
        DefinitionSlot { SlotKind: DefinitionSlotKind.FootnoteContent } content => content with { Text = content.Text.TrimStart(' ', '\t') },
        ContainerBlock container => container with { Children = [.. container.Children.Select(Trim)] },
        _ => block,
    };

    private static string Snapshot(Document document) =>
        MarkdownSerializer.SerializeNormalized(document) + "\n----\n" + ModelDump.Structure(document)
        + string.Join(",", All(document).Select(static block => block.Id.ToString()));

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

    private static IEnumerable<LeafBlock> Leaves(Block block) => All(block).OfType<LeafBlock>();

    [GeneratedRegex(@"^\[[ xX]\][ \t]")]
    private static partial Regex TaskMarker();

    [GeneratedRegex(@"^ {0,3}(?:(?:-[ \t]*){3,}|(?:\*[ \t]*){3,}|(?:_[ \t]*){3,})$")]
    private static partial Regex RuleLine();
}
