using System.Text;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.Projection;
using Quill.Editor.State;

namespace Quill.Editor.Editing;

/// <summary>
/// Navigation inside the document (07: Ctrl+click and footnote backlinks): footnote definitions and
/// references, and headings by anchor. These scan the document; they run on a click, not per key.
/// </summary>
internal static class DocumentNavigation
{
    /// <summary>The start of the description of the footnote's definition (the first one with the label).</summary>
    public static Position? FootnoteDefinition(Document document, string label)
    {
        var id = ReferenceIndex.Of(document).ResolveFootnote(document, LinkLabel.Normalize(label));
        return id is { } found && DocumentTree.TryFind(document, found) is FootnoteDefinition definition
            ? new TextPosition(definition.Content.Id, 0)
            : null;
    }

    /// <summary>The footnote reference with the label and index in document order, before its <c>[^</c>.</summary>
    public static Position? FootnoteReference(Document document, string label, int index)
    {
        string normalized = LinkLabel.Normalize(label);
        var references = ReferenceIndex.Of(document);
        int seen = 0;
        foreach (var leaf in TextLeaves(document))
        {
            foreach (var node in InlineTrees.Get(references, leaf).Descendants())
            {
                if (node.Kind == InlineKind.FootnoteReference && LinkLabel.Normalize(node.Label ?? string.Empty) == normalized && seen++ == index)
                {
                    return new TextPosition(leaf.Id, node.Range.Start);
                }
            }
        }

        return null;
    }

    /// <summary>The start of the first heading whose anchor is the fragment (GitHub-style slugs, case-insensitive).</summary>
    public static Position? Anchor(Document document, string fragment)
    {
        string wanted = Uri.UnescapeDataString(fragment.TrimStart('#')).ToLowerInvariant();
        var references = ReferenceIndex.Of(document);
        foreach (var leaf in TextLeaves(document))
        {
            if (leaf is Heading heading && Slug(PlainText.Of(heading, references)) == wanted)
            {
                return new TextPosition(heading.Id, 0);
            }
        }

        return null;
    }

    /// <summary>A heading's anchor: lowercase, spaces to hyphens, punctuation other than <c>-</c> and <c>_</c> removed.</summary>
    public static string Slug(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c is '-' or '_')
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c))
            {
                builder.Append('-');
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<TextLeafBlock> TextLeaves(Block block)
    {
        switch (block)
        {
            case TextLeafBlock leaf:
                yield return leaf;
                break;
            case ContainerBlock container:
                foreach (var child in container.Children)
                {
                    foreach (var leaf in TextLeaves(child))
                    {
                        yield return leaf;
                    }
                }

                break;
        }
    }
}
