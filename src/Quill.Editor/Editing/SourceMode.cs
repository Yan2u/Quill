using Quill.Editor.Documents;
using Quill.Editor.Markdown;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// An excursion into the source mode (11-source-mode.md): the document it left, its source text and
/// where each block starts in it, and the source document the source session edits.
/// </summary>
public sealed record SourceSession(Document Original, string Text, IReadOnlyDictionary<BlockId, int> Starts, EditorState Source);

/// <summary>One undo item of a source session, as a change of the source text (06: source edits in the history).</summary>
public sealed record SourceEdit(int Start, string Removed, string Inserted, int AnchorBefore, int HeadBefore, int AnchorAfter, int HeadAfter);

/// <summary>
/// Switching between the editing and the source mode (06, 11-source-mode.md): source text with block
/// offsets, positions mapped both ways through the text of each leaf, and the document rebuilt from
/// edited text with the unchanged top-level blocks kept as they were (same objects, same IDs).
/// </summary>
public static class SourceMode
{
    /// <summary>The source session of a document; <paramref name="selection"/> is mapped into the source.</summary>
    public static SourceSession Enter(Document document, TextSelection selection, IBlockIdSource ids, EditorOptions options)
    {
        string text = MarkdownSerializer.SerializeNormalized(document, out var starts, options);
        var source = SourceText.ToDocument(text, document.Format, ids);
        var segments = Segments(document, text, starts);
        var mapped = new TextSelection(LinePosition(source, ToOffset(document, segments, text, selection.Anchor)), LinePosition(source, ToOffset(document, segments, text, selection.Head)));
        return new SourceSession(document, text, starts, new EditorState { Document = source, Selection = mapped });
    }

    /// <summary>
    /// The document for edited source text: imported, with the top-level blocks whose text did not change
    /// (from the start and from the end) taken from <paramref name="original"/>. When that does not write
    /// back the same text, the plain import.
    /// </summary>
    public static Document Rebuild(Document original, string originalText, IReadOnlyDictionary<BlockId, int> originalStarts, string text, IBlockIdSource ids, EditorOptions options)
    {
        var imported = MarkdownImporter.Import(text, ids).Document;
        var format = original.Format with { EndsWithLineBreak = imported.Format.EndsWithLineBreak };
        long revision = original.Revision + 1;
        string importedText = MarkdownSerializer.SerializeNormalized(imported, out var importedStarts, options);
        var fallback = imported with { Format = format, Revision = revision };
        if (importedText != text)
        {
            return fallback;
        }

        var oldBlocks = original.Children.ToList();
        var newBlocks = imported.Children.ToList();
        string OldSlice(int i) => Slice(originalText, originalStarts, oldBlocks, i);
        string NewSlice(int i) => Slice(text, importedStarts, newBlocks, i);

        int prefix = 0;
        while (prefix < oldBlocks.Count && prefix < newBlocks.Count && OldSlice(prefix) == NewSlice(prefix))
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < oldBlocks.Count - prefix && suffix < newBlocks.Count - prefix
            && OldSlice(oldBlocks.Count - 1 - suffix) == NewSlice(newBlocks.Count - 1 - suffix))
        {
            suffix++;
        }

        var children = new List<Block>(newBlocks.Count);
        children.AddRange(oldBlocks.Take(prefix));
        children.AddRange(newBlocks.Skip(prefix).Take(newBlocks.Count - prefix - suffix));
        children.AddRange(oldBlocks.Skip(oldBlocks.Count - suffix));

        // A separator names the block before it: the first new block follows a kept one. The first kept
        // block after the change keeps its object when its separator writes the same either way.
        Reattach(children, prefix);
        var rebuilt = original with { Children = [.. children], Format = format, Revision = revision };
        if (MarkdownSerializer.SerializeNormalized(rebuilt, options) == text)
        {
            return rebuilt;
        }

        Reattach(children, children.Count - suffix);
        rebuilt = original with { Children = [.. children], Format = format, Revision = revision };
        return MarkdownSerializer.SerializeNormalized(rebuilt, options) == text ? rebuilt : fallback;
    }

    /// <summary>The editing-mode selection for a selection of the source, through the text of the rebuilt document.</summary>
    public static TextSelection MapBack(Document document, string text, TextSelection sourceSelection, Document source, EditorOptions options)
    {
        string written = MarkdownSerializer.SerializeNormalized(document, out var starts, options);
        var segments = Segments(document, written, starts);
        return new TextSelection(FromOffset(document, segments, OffsetOf(source, sourceSelection.Anchor)), FromOffset(document, segments, OffsetOf(source, sourceSelection.Head)));
    }

    /// <summary>A selection of the document for offsets of its source text.</summary>
    public static TextSelection SelectionAt(Document document, int anchor, int head, EditorOptions options)
    {
        string written = MarkdownSerializer.SerializeNormalized(document, out var starts, options);
        var segments = Segments(document, written, starts);
        return new TextSelection(FromOffset(document, segments, anchor), FromOffset(document, segments, head));
    }

    /// <summary>The offset in the source text of a position of a source document.</summary>
    public static int OffsetOf(Document source, Position position)
    {
        if (position is not TextPosition at)
        {
            return 0;
        }

        int offset = 0;
        foreach (var block in source.Children)
        {
            var line = (LeafBlock)block;
            if (line.Id == at.Block)
            {
                return offset + Math.Min(at.Offset, line.Text.Length);
            }

            offset += line.Text.Length + 1;
        }

        return offset;
    }

    /// <summary>The change from <paramref name="before"/> to <paramref name="after"/>: the common start and end kept.</summary>
    public static (int Start, string Removed, string Inserted) Diff(string before, string after)
    {
        int prefix = before.AsSpan().CommonPrefixLength(after);
        int limit = Math.Min(before.Length, after.Length) - prefix;
        int suffix = 0;
        while (suffix < limit && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        return (prefix, before[prefix..(before.Length - suffix)], after[prefix..(after.Length - suffix)]);
    }

    /// <summary>The position in a source document at an offset of its text.</summary>
    public static TextPosition LinePosition(Document source, int offset)
    {
        LeafBlock? last = null;
        foreach (var block in source.Children)
        {
            var line = (LeafBlock)block;
            if (offset <= line.Text.Length)
            {
                return new TextPosition(line.Id, Math.Max(0, offset));
            }

            offset -= line.Text.Length + 1;
            last = line;
        }

        return new TextPosition(last!.Id, last.Text.Length);
    }

    private static string Slice(string text, IReadOnlyDictionary<BlockId, int> starts, List<Block> blocks, int index)
    {
        int start = starts.TryGetValue(blocks[index].Id, out int s) ? s : 0;
        int end = index + 1 < blocks.Count && starts.TryGetValue(blocks[index + 1].Id, out int e) ? e : text.Length;
        return text[start..Math.Max(start, end)];
    }

    /// <summary>Points the separator of the block at <paramref name="index"/> to the block now before it.</summary>
    private static void Reattach(List<Block> children, int index)
    {
        if (index <= 0 || index >= children.Count || children[index].SeparatorBefore is not { } separator)
        {
            return;
        }

        children[index] = children[index] with { SeparatorBefore = separator with { Previous = children[index - 1].Id } };
    }

    /// <summary>A line of a leaf in the source text: the leaf offset where it starts and where it is written.</summary>
    private readonly record struct Segment(BlockId Leaf, int LeafOffset, int TextOffset, int Length);

    /// <summary>
    /// Where the lines of every leaf are written: from the block's start, each line is looked for on the
    /// output lines that follow (fences and markers come before the text); a line not found is placed
    /// where the search started.
    /// </summary>
    private static List<Segment> Segments(Document document, string text, IReadOnlyDictionary<BlockId, int> starts)
    {
        var segments = new List<Segment>();
        int cursor = 0;
        foreach (var leaf in Navigation.Leaves(document))
        {
            int position = Math.Max(cursor, starts.TryGetValue(leaf.Id, out int start) ? start : cursor);
            int leafOffset = 0;
            foreach (string line in leaf.Text.Split('\n'))
            {
                int found = position;
                if (line.Length > 0)
                {
                    int window = position;
                    for (int i = 0; i < 3 && window < text.Length; i++)
                    {
                        int next = text.IndexOf('\n', window);
                        window = next < 0 ? text.Length : next + 1;
                    }

                    int at = text.IndexOf(line, position, Math.Max(0, window - position), StringComparison.Ordinal);
                    found = at >= 0 ? at : position;
                }

                segments.Add(new Segment(leaf.Id, leafOffset, found, line.Length));
                int lineEnd = text.IndexOf('\n', found + line.Length);
                position = lineEnd < 0 ? text.Length : lineEnd + 1;
                leafOffset += line.Length + 1;
            }

            cursor = segments.Count > 0 ? segments[^1].TextOffset + segments[^1].Length : cursor;
        }

        return segments;
    }

    private static int ToOffset(Document document, List<Segment> segments, string text, Position position)
    {
        switch (position)
        {
            case TextPosition at:
                Segment? best = null;
                foreach (var segment in segments)
                {
                    if (segment.Leaf == at.Block && segment.LeafOffset <= at.Offset)
                    {
                        best = segment;
                    }
                }

                return best is { } line ? line.TextOffset + Math.Min(at.Offset - line.LeafOffset, line.Length) : 0;
            case BoundaryPosition boundary when DocumentTree.TryFind(document, boundary.Parent) is ContainerBlock container
                && boundary.Index < container.Children.Count && Navigation.FirstLeaf(container.Children[boundary.Index]) is { } next:
                // Before the block after the boundary (a virtual paragraph has no text of its own).
                foreach (var segment in segments)
                {
                    if (segment.Leaf == next.Id)
                    {
                        return segment.TextOffset;
                    }
                }

                return text.Length;
            case BoundaryPosition:
                return text.Length;
            default:
                return 0;
        }
    }

    private static Position FromOffset(Document document, List<Segment> segments, int offset)
    {
        if (segments.Count == 0)
        {
            return document.Children.Count > 0 ? Navigation.First(document, document) : new BoundaryPosition(document.Id, 0);
        }

        // The line holding the offset, or the first line after it (markers and fences are not text).
        foreach (var segment in segments)
        {
            if (offset <= segment.TextOffset + segment.Length)
            {
                int within = Math.Clamp(offset - segment.TextOffset, 0, segment.Length);
                return new TextPosition(segment.Leaf, segment.LeafOffset + within);
            }
        }

        var last = segments[^1];
        return new TextPosition(last.Leaf, last.LeafOffset + last.Length);
    }
}
