using Quill.Editor.Documents;
using Quill.Editor.Editing;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;

namespace Quill.Editor.Statistics;

/// <summary>A heading of the outline: its block, level and displayed text (no syntax).</summary>
public sealed record OutlineEntry(BlockId Id, int Level, string Text);

/// <summary>
/// The outline sidebar (M6.5): the headings in document order from the heading index the TOC keeps
/// (Q68), and the current heading: the last one at or before the first block in view (Typora follows
/// the scroll position).
/// </summary>
public static class DocumentOutline
{
    public static IReadOnlyList<OutlineEntry> Of(Document document)
    {
        var references = ReferenceIndex.Of(document);
        return [.. HeadingIndex.Of(document).InOrder(document)
            .Select(heading => new OutlineEntry(heading.Id, heading.Level, PlainText.Of(heading, references).Replace('\n', ' ').Trim()))];
    }

    /// <summary>The heading whose section holds <paramref name="block"/> (the first block in view), or null before the first heading.</summary>
    public static BlockId? CurrentHeading(Document document, IReadOnlyList<OutlineEntry> outline, BlockId block)
    {
        if (!DocumentTree.Contains(document, block) || Navigation.Leaves(DocumentTree.Find(document, block)).FirstOrDefault() is not { } leaf)
        {
            return null;
        }

        var at = new TextPosition(leaf.Id, 0);
        BlockId? current = null;
        foreach (var entry in outline)
        {
            if (!DocumentTree.Contains(document, entry.Id) || PositionOrder.Compare(document, new TextPosition(entry.Id, 0), at) > 0)
            {
                break;
            }

            current = entry.Id;
        }

        return current;
    }
}
