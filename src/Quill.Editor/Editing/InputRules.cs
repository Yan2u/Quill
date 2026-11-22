using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// Input rules (04-commands-and-behaviors.md). After any edit, a paragraph whose start matches block
/// syntax turns into that block at once; Enter turns a paragraph that is only a code fence, "$$", a
/// rule, a table header row or "[toc]" into that block, and "---" as the first block into front matter. No escaping (Q24). Each conversion is a
/// transaction of its own, so the first undo reverts only the conversion.
/// </summary>
internal static partial class InputRules
{
    /// <summary>Applies the paragraph-start rules to the paragraph, if it matches.</summary>
    public static Transaction? TryConvert(EditorState state, BlockId paragraphId, IBlockIdSource ids, DateTimeOffset now)
    {
        if (!DocumentTree.Contains(state.Document, paragraphId) || DocumentTree.Find(state.Document, paragraphId) is not Paragraph paragraph)
        {
            return null;
        }

        var builder = new TransactionBuilder(state, ids);
        string text = paragraph.Text;
        int caret = state.Selection.Head is TextPosition head && head.Block == paragraphId ? head.Offset : -1;

        var location = DocumentTree.Locate(state.Document, paragraphId);
        if (location.Parent is ListItem { Task: TaskState.None } item && location.Index == 0 && TaskPattern().Match(text) is { Success: true } task)
        {
            // "[ ] " at the start of a list item's first paragraph makes it a task item.
            builder.ReplaceText(paragraphId, 0, task.Length, string.Empty);
            var updated = builder.Find<ListItem>(item.Id) with { Task = task.Groups[1].Value == " " ? TaskState.Unchecked : TaskState.Checked };
            builder.ReplaceBlock(updated);
            return Finish(builder, paragraphId, caret, task.Length, now);
        }

        if (HeadingPattern().Match(text) is { Success: true } heading)
        {
            int level = heading.Groups[1].Length;
            string rest = text[heading.Length..];
            int lineEnd = rest.IndexOf('\n');
            var converted = new Heading
            {
                Id = paragraphId,
                Level = level,
                Text = lineEnd < 0 ? rest : rest[..lineEnd],
                SeparatorBefore = paragraph.SeparatorBefore,
                LinePrefixes = paragraph.LinePrefixes.IsEmpty ? [] : [paragraph.LinePrefixes[0]],
            };
            builder.ReplaceBlock(converted);
            if (lineEnd >= 0)
            {
                // Headings hold one line; the lines after it stay a paragraph.
                EditOperations.InsertAfter(builder, paragraphId, new Paragraph { Id = builder.NewId(), Text = rest[(lineEnd + 1)..] });
            }

            return Finish(builder, paragraphId, caret, heading.Length, now);
        }

        if (QuotePattern().Match(text) is { Success: true } quote)
        {
            var inner = paragraph with { Text = text[quote.Length..], SeparatorBefore = null, LinePrefixes = [] };
            Wrap(builder, location, paragraph, new BlockQuote { Id = builder.NewId(), Children = [inner] });
            return Finish(builder, paragraphId, caret, quote.Length, now);
        }

        // In the first paragraph of a list item a list starts right there, as a sub-list (Q48).
        if (BulletPattern().Match(text) is { Success: true } bullet)
        {
            var taskMatch = TaskPattern().Match(text[bullet.Length..]);
            int length = bullet.Length + (taskMatch.Success ? taskMatch.Length : 0);
            var taskState = length > bullet.Length ? (taskMatch.Groups[1].Value == " " ? TaskState.Unchecked : TaskState.Checked) : TaskState.None;
            var inner = paragraph with { Text = text[length..], SeparatorBefore = null, LinePrefixes = [] };
            var listItem = new ListItem { Id = builder.NewId(), Children = [inner], Task = taskState };
            var list = new ListBlock { Id = builder.NewId(), IsOrdered = false, Children = [listItem], Syntax = new ListSyntax(bullet.Groups[1].Value[0], WasLoose: false) };
            Wrap(builder, location, paragraph, list);
            return Finish(builder, paragraphId, caret, length, now);
        }

        if (OrderedPattern().Match(text) is { Success: true } ordered)
        {
            int number = int.Parse(ordered.Groups[1].Value, CultureInfo.InvariantCulture);
            var inner = paragraph with { Text = text[ordered.Length..], SeparatorBefore = null, LinePrefixes = [] };
            var listItem = new ListItem { Id = builder.NewId(), Children = [inner], Number = number };
            var list = new ListBlock { Id = builder.NewId(), IsOrdered = true, Children = [listItem], Syntax = new ListSyntax(ordered.Groups[2].Value[0], WasLoose: false) };
            Wrap(builder, location, paragraph, list);
            return Finish(builder, paragraphId, caret, ordered.Length, now);
        }

        return null;
    }

    /// <summary>Applies the Enter rules to a single-line paragraph with the caret at its end.</summary>
    public static Transaction? TryConvertOnEnter(EditorState state, IBlockIdSource ids, DateTimeOffset now)
    {
        if (state.Selection.Head is not TextPosition head || !state.Selection.IsCollapsed
            || DocumentTree.Find(state.Document, head.Block) is not Paragraph paragraph
            || head.Offset != paragraph.Text.Length || paragraph.Text.Contains('\n'))
        {
            return null;
        }

        string text = paragraph.Text.Trim(' ');
        var builder = new TransactionBuilder(state, ids);
        var location = DocumentTree.Locate(state.Document, paragraph.Id);
        Block? converted = null;
        Position? caret = null;
        bool paragraphAfter = false;

        if (FencePattern().Match(text) is { Success: true } fence)
        {
            var code = CodeFence.Create(builder.NewId, string.Empty, fence.Groups[2].Value.Trim());
            converted = code;
            caret = new TextPosition(code.Code.Id, 0);
        }
        else if (text == "$$")
        {
            var math = new MathBlock { Id = builder.NewId(), Text = string.Empty };
            converted = math;
            caret = new TextPosition(math.Id, 0);
        }
        else if (paragraph.Text == "---" && location.Parent is Document && location.Index == 0)
        {
            // "---" then Enter as the first block of the document starts YAML front matter (as Typora);
            // typing "---" and moving the caret away still makes a thematic break.
            var front = new FrontMatter { Id = builder.NewId(), Text = string.Empty };
            converted = front;
            caret = new TextPosition(front.Id, 0);
        }
        else if (RulePattern().IsMatch(text))
        {
            converted = new ThematicBreak { Id = builder.NewId(), Syntax = new SyntaxLine(text, LinePrefix.None) };
            paragraphAfter = true;
        }
        else if (text.Equals("[toc]", StringComparison.OrdinalIgnoreCase))
        {
            converted = new TocBlock { Id = builder.NewId(), Syntax = new SyntaxLine(text, LinePrefix.None) };
            paragraphAfter = true;
        }
        else if (TableHeaderPattern().IsMatch(text))
        {
            var cells = text.Trim('|').Split('|').Select(static cell => cell.Trim()).ToList();
            var header = NewRow(builder, cells);
            var body = NewRow(builder, cells.Select(static _ => string.Empty).ToList());
            converted = new Table
            {
                Id = builder.NewId(),
                Alignments = [.. cells.Select(static _ => TableAlignment.None)],
                Children = [header, body],
            };
            caret = new TextPosition(((TableRow)body).Children[0].Id, 0);
        }

        if (converted is null)
        {
            return null;
        }

        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
        builder.InsertBlocks(location.Parent.Id, location.Index, converted with { SeparatorBefore = paragraph.SeparatorBefore });
        if (paragraphAfter)
        {
            var next = new Paragraph { Id = builder.NewId(), Text = string.Empty };
            builder.InsertBlocks(location.Parent.Id, location.Index + 1, next);
            caret = new TextPosition(next.Id, 0);
        }

        builder.SetCaret(caret!);
        return builder.Build(TransactionKind.InputRule, now);
    }

    private static Block NewRow(TransactionBuilder builder, IReadOnlyList<string> cells) => new TableRow
    {
        Id = builder.NewId(),
        Children = [.. cells.Select(text => (Block)new TableCell { Id = builder.NewId(), Text = text })],
    };

    /// <summary>Puts a container in place of the paragraph; the paragraph keeps its ID inside it.</summary>
    private static void Wrap(TransactionBuilder builder, BlockLocation location, Paragraph paragraph, ContainerBlock container)
    {
        builder.RemoveBlocks(location.Parent!.Id, location.Index, 1);
        builder.InsertBlocks(location.Parent.Id, location.Index, container with { SeparatorBefore = paragraph.SeparatorBefore });
    }

    private static Transaction? Finish(TransactionBuilder builder, BlockId paragraphId, int caret, int removed, DateTimeOffset now)
    {
        builder.SetCaret(paragraphId, Math.Max(0, caret - removed));
        return builder.Build(TransactionKind.InputRule, now);
    }

    [GeneratedRegex(@"^(#{1,6}) ")]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"^> ")]
    private static partial Regex QuotePattern();

    [GeneratedRegex(@"^([-*+]) ")]
    private static partial Regex BulletPattern();

    [GeneratedRegex(@"^\[([ xX])\] ")]
    private static partial Regex TaskPattern();

    [GeneratedRegex(@"^(\d{1,9})([.)]) ")]
    private static partial Regex OrderedPattern();

    [GeneratedRegex(@"^(`{3,}|~{3,})([^`]*)$")]
    private static partial Regex FencePattern();

    [GeneratedRegex(@"^(?:(?:\* *){3,}|(?:- *){3,}|(?:_ *){3,})$")]
    private static partial Regex RulePattern();

    [GeneratedRegex(@"^\|(?:[^|]*\|){1,}$")]
    private static partial Regex TableHeaderPattern();
}
