using Quill.Editor.Behaviors;
using Quill.Editor.Commands;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;
using Quill.Editor.Transactions;

namespace Quill.Editor.Editing;

/// <summary>
/// The Format menu (M6.0, Typora's shortcuts, behavior confirmed by the user): inline formats toggled
/// on the selection, links, images and clearing formats. Every command is one undo item.
/// <list type="bullet">
/// <item>A caret alone inside an element of the format removes the whole element; elsewhere it inserts
/// both markers with the caret between them, so what is typed next has the format.</item>
/// <item>A selection that is an element of the format (its content or all of it), or lies inside one,
/// removes that element; otherwise the selection gets the format, merged with the elements of the same
/// format it overlaps, without the spaces at its ends (they would keep the markers from pairing).</item>
/// <item>A selection across blocks formats each text block's part separately.</item>
/// </list>
/// </summary>
internal static class InlineFormatting
{
    public static Transaction? Toggle(EditorState state, InlineFormat format, BehaviorContext context)
    {
        var parts = Parts(state);
        if (parts.Count == 0 || parts.Any(part => !Allows(part.Leaf, format)))
        {
            return null;
        }

        var builder = context.Begin(state);
        bool collapsed = state.Selection.IsCollapsed;
        (BlockId Block, int Offset)? first = null, last = null;
        foreach (var (leaf, start, end) in parts)
        {
            var (newStart, newEnd) = ToggleInLeaf(builder, state.Document, leaf, start, end, format, collapsed || parts.Count == 1 ? collapsed : false, single: parts.Count == 1);
            first ??= (leaf.Id, newStart);
            last = (leaf.Id, newEnd);
        }

        builder.Selection = new TextSelection(new TextPosition(first!.Value.Block, first.Value.Offset), new TextPosition(last!.Value.Block, last.Value.Offset, collapsed ? Affinity.Forward : Affinity.Backward));
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>Ctrl+K: <c>[selection](url)</c>; a caret or selection in a link removes the link〔默认〕.</summary>
    public static Transaction? Link(EditorState state, string? url, bool image, BehaviorContext context)
    {
        var parts = Parts(state);
        if (parts.Count != 1 || parts[0].Leaf.InlineSyntax != InlineSyntaxSet.Full)
        {
            return null;
        }

        var (leaf, start, end) = parts[0];
        var builder = context.Begin(state);
        var tree = InlineTrees.Get(state.Document, leaf);
        var kind = image ? InlineKind.Image : InlineKind.Link;
        var existing = tree.Descendants()
            .Where(node => node.Kind == kind && node.Range.Start <= start && end <= node.Range.End && (start > node.Range.Start || end < node.Range.End || start != end))
            .OrderBy(node => node.Range.Length)
            .FirstOrDefault();
        if (existing is not null && !image)
        {
            // Keep the link text, drop the brackets and destination.
            string text = leaf.Text[existing.Content.Start..existing.Content.End];
            builder.ReplaceText(leaf.Id, existing.Range.Start, existing.Range.End, text);
            int Map(int offset) => existing.Range.Start + Math.Clamp(offset - existing.Content.Start, 0, text.Length);
            builder.Selection = start == end
                ? TextSelection.Caret(leaf.Id, Map(start))
                : new TextSelection(new TextPosition(leaf.Id, Map(start)), new TextPosition(leaf.Id, Map(end), Affinity.Backward));
            return builder.Build(TransactionKind.Edit, context.Now);
        }

        string label = leaf.Text[start..end];
        string destination = url ?? string.Empty;
        string inserted = $"{(image ? "!" : string.Empty)}[{label}]({destination})";
        builder.ReplaceText(leaf.Id, start, end, inserted);

        // With text the destination is what is missing; without text, the text.
        int caret = label.Length == 0 && url is null
            ? start + (image ? 2 : 1)
            : start + inserted.Length - 1;
        builder.SetCaret(leaf.Id, caret);
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>
    /// Ctrl+\: removes the inline formats (emphasis, strong, strikethrough, highlight, scripts, code,
    /// underline and the other paired tags) that touch the selection, or that hold the caret〔默认〕. Links,
    /// images and math stay.
    /// </summary>
    public static Transaction? Clear(EditorState state, BehaviorContext context)
    {
        var parts = Parts(state);
        if (parts.Count == 0)
        {
            return null;
        }

        var builder = context.Begin(state);
        bool changed = false;
        (BlockId Block, int Offset)? first = null, last = null;
        foreach (var (leaf, start, end) in parts)
        {
            var tree = InlineTrees.Get(state.Document, leaf);
            var nodes = tree.Descendants()
                .Where(node => IsStyle(node.Kind) && (start == end ? node.Range.Start < start && start < node.Range.End : node.Range.End > start && node.Range.Start < end))
                .ToList();
            var map = new Deletions();
            foreach (var node in nodes)
            {
                map.Add(node.Opening);
                map.Add(node.Closing);
            }

            map.Apply(builder, leaf.Id);
            changed |= nodes.Count > 0;
            first ??= (leaf.Id, map.Map(start));
            last = (leaf.Id, map.Map(end));
        }

        if (!changed)
        {
            return null;
        }

        builder.Selection = new TextSelection(new TextPosition(first!.Value.Block, first.Value.Offset), new TextPosition(last!.Value.Block, last.Value.Offset, Affinity.Backward));
        return builder.Build(TransactionKind.Edit, context.Now);
    }

    /// <summary>The kinds Ctrl+\ removes.</summary>
    private static bool IsStyle(InlineKind kind) => kind is InlineKind.Strong or InlineKind.Emphasis or InlineKind.Strikethrough or InlineKind.Highlight
        or InlineKind.Subscript or InlineKind.Superscript or InlineKind.Code or InlineKind.HtmlSpan;

    private static bool Allows(TextLeafBlock leaf, InlineFormat format) => leaf.InlineSyntax switch
    {
        InlineSyntaxSet.Full => true,
        InlineSyntaxSet.FootnoteContent => format is InlineFormat.Strong or InlineFormat.Emphasis or InlineFormat.Math
            or InlineFormat.Superscript or InlineFormat.Subscript or InlineFormat.Code,
        _ => false,
    };

    private static (int Start, int End) ToggleInLeaf(TransactionBuilder builder, Document document, TextLeafBlock leaf, int start, int end, InlineFormat format, bool collapsed, bool single)
    {
        string text = leaf.Text;
        var elements = Elements(document, leaf, format);
        if (collapsed)
        {
            var around = elements.Where(element => element.Range.Start < start && start < element.Range.End).OrderBy(element => element.Range.Length).FirstOrDefault();
            if (around is not null)
            {
                var map = Unwrap(builder, leaf.Id, around);
                int caret = map.Map(start);
                return (caret, caret);
            }

            var (open, close) = Markers(format, string.Empty);
            builder.InsertText(leaf.Id, start, open + close);
            return (start + open.Length, start + open.Length);
        }

        // Only a single part removes a format; across blocks every part gets it (Typora formats each block).
        var exact = single
            ? elements.Where(element => (element.Content.Start == start && element.Content.End == end) || (element.Range.Start == start && element.Range.End == end)
                    || (element.Content.Start <= start && end <= element.Content.End))
                .OrderBy(element => element.Range.Length)
                .FirstOrDefault()
            : null;
        if (exact is not null)
        {
            var map = Unwrap(builder, leaf.Id, exact);
            return (map.Map(Math.Max(start, exact.Content.Start)), map.Map(Math.Min(end, exact.Content.End)));
        }

        // Merge with the overlapped elements of the same format, then wrap without the spaces at the ends.
        int unionStart = start, unionEnd = end;
        var overlapped = elements.Where(element => element.Range.End > start && element.Range.Start < end).ToList();
        foreach (var element in overlapped)
        {
            unionStart = Math.Min(unionStart, element.Range.Start);
            unionEnd = Math.Max(unionEnd, element.Range.End);
        }

        var deletions = new Deletions();
        foreach (var element in overlapped)
        {
            deletions.Add(element.Opening);
            deletions.Add(element.Closing);
        }

        deletions.Apply(builder, leaf.Id);
        string after = builder.Find<LeafBlock>(leaf.Id).Text;
        int from = deletions.Map(unionStart);
        int to = deletions.Map(unionEnd);
        if (Trims(format))
        {
            while (from < to && char.IsWhiteSpace(after[from]))
            {
                from++;
            }

            while (to > from && char.IsWhiteSpace(after[to - 1]))
            {
                to--;
            }
        }

        if (to == from)
        {
            return (from, to);
        }

        var (opening, closing) = Markers(format, after[from..to]);
        builder.InsertText(leaf.Id, to, closing);
        builder.InsertText(leaf.Id, from, opening);
        return (from + opening.Length, to + opening.Length);
    }

    /// <summary>Elements of a format in a leaf: their whole range, content and markers.</summary>
    private static List<Element> Elements(Document document, TextLeafBlock leaf, InlineFormat format)
    {
        var tree = InlineTrees.Get(document, leaf);
        var elements = new List<Element>();
        foreach (var node in tree.Descendants())
        {
            if (format == InlineFormat.Comment)
            {
                string source = leaf.Text[node.Range.Start..node.Range.End];
                if (node.Kind == InlineKind.Html && source.StartsWith("<!--", StringComparison.Ordinal) && source.EndsWith("-->", StringComparison.Ordinal) && source.Length >= 7)
                {
                    elements.Add(new Element(node.Range, new TextRange(node.Range.Start + 4, node.Range.End - 3)));
                }

                continue;
            }

            bool matches = format switch
            {
                InlineFormat.Strong => node.Kind == InlineKind.Strong,
                InlineFormat.Emphasis => node.Kind == InlineKind.Emphasis,
                InlineFormat.Strikethrough => node.Kind == InlineKind.Strikethrough,
                InlineFormat.Code => node.Kind == InlineKind.Code,
                InlineFormat.Math => node.Kind == InlineKind.Math,
                InlineFormat.Superscript => node.Kind == InlineKind.Superscript,
                InlineFormat.Subscript => node.Kind == InlineKind.Subscript,
                InlineFormat.Underline => node.Kind == InlineKind.HtmlSpan && string.Equals(node.Value, "u", StringComparison.OrdinalIgnoreCase),
                _ => false,
            };
            if (matches)
            {
                elements.Add(new Element(node.Range, node.Content));
            }
        }

        return elements;
    }

    private static Deletions Unwrap(TransactionBuilder builder, BlockId leaf, Element element)
    {
        var deletions = new Deletions();
        deletions.Add(element.Opening);
        deletions.Add(element.Closing);
        deletions.Apply(builder, leaf);
        return deletions;
    }

    /// <summary>The markers of a format around <paramref name="content"/>.</summary>
    private static (string Open, string Close) Markers(InlineFormat format, string content)
    {
        switch (format)
        {
            case InlineFormat.Strong:
                return ("**", "**");
            case InlineFormat.Emphasis:
                return ("*", "*");
            case InlineFormat.Strikethrough:
                return ("~~", "~~");
            case InlineFormat.Math:
                return ("$", "$");
            case InlineFormat.Superscript:
                return ("^", "^");
            case InlineFormat.Subscript:
                return ("~", "~");
            case InlineFormat.Underline:
                return ("<u>", "</u>");
            case InlineFormat.Comment:
                return ("<!--", "-->");
            default:
            {
                // A code span needs a longer run of backticks than it contains, and a space where the content touches one.
                int longest = 0, run = 0;
                foreach (char c in content)
                {
                    run = c == '`' ? run + 1 : 0;
                    longest = Math.Max(longest, run);
                }

                string fence = new('`', longest + 1);
                bool pad = content.StartsWith('`') || content.EndsWith('`');
                return pad ? (fence + " ", " " + fence) : (fence, fence);
            }
        }
    }

    /// <summary>Formats whose markers only pair next to non-space characters.</summary>
    private static bool Trims(InlineFormat format) => format is not (InlineFormat.Code or InlineFormat.Underline or InlineFormat.Comment);

    /// <summary>The text leaves of the selection and the range of each, in document order; a caret gives one empty range.</summary>
    private static List<(TextLeafBlock Leaf, int Start, int End)> Parts(EditorState state)
    {
        var parts = new List<(TextLeafBlock, int, int)>();
        var (start, end) = PositionOrder.Ordered(state.Document, state.Selection);
        if (start is not TextPosition first || end is not TextPosition last)
        {
            return parts;
        }

        if (first.Block == last.Block)
        {
            if (DocumentTree.TryFind(state.Document, first.Block) is TextLeafBlock only)
            {
                parts.Add((only, first.Offset, last.Offset));
            }

            return parts;
        }

        for (var leaf = DocumentTree.TryFind(state.Document, first.Block) as LeafBlock; leaf is not null; leaf = Navigation.AdjacentLeaf(state.Document, leaf.Id, forward: true))
        {
            int from = leaf.Id == first.Block ? first.Offset : 0;
            int to = leaf.Id == last.Block ? last.Offset : leaf.Text.Length;
            if (leaf is TextLeafBlock text && to > from)
            {
                parts.Add((text, from, to));
            }

            if (leaf.Id == last.Block)
            {
                break;
            }
        }

        return parts;
    }

    private sealed record Element(TextRange Range, TextRange Content)
    {
        public TextRange Opening => new(Range.Start, Content.Start);

        public TextRange Closing => new(Content.End, Range.End);
    }

    /// <summary>Ranges deleted from one leaf, and how offsets of the text before map to the text after.</summary>
    private sealed class Deletions
    {
        private readonly List<TextRange> _ranges = [];

        public void Add(TextRange range)
        {
            if (range.Length > 0 && !_ranges.Contains(range))
            {
                _ranges.Add(range);
            }
        }

        /// <summary>Deletes the ranges, last first, so the earlier offsets stay valid.</summary>
        public void Apply(TransactionBuilder builder, BlockId leaf)
        {
            foreach (var range in _ranges.OrderByDescending(static range => range.Start))
            {
                builder.ReplaceText(leaf, range.Start, range.End, string.Empty);
            }
        }

        public int Map(int offset)
        {
            int mapped = offset;
            foreach (var range in _ranges)
            {
                if (offset >= range.End)
                {
                    mapped -= range.Length;
                }
                else if (offset > range.Start)
                {
                    mapped -= offset - range.Start;
                }
            }

            return mapped;
        }
    }
}
