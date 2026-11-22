using System.Runtime.CompilerServices;
using Quill.Editor.Documents;
using Quill.Editor.Markdown;

namespace Quill.Editor.Inlines;

/// <summary>
/// The inline tree of a text leaf, a derived quantity (03-editor-state.md). Trees are cached per leaf
/// node: leaves are immutable, so an edited leaf is a new node and parses again, while untouched
/// leaves keep their tree. A cached tree is reused for a later document version when the link labels
/// it depends on are still defined the same way (06-markdown-io.md).
/// </summary>
public static class InlineTrees
{
    private static readonly ConditionalWeakTable<TextLeafBlock, Entry> Cache = new();

    public static InlineTree Get(Document document, TextLeafBlock leaf) => Get(ReferenceIndex.Of(document), leaf);

    public static InlineTree Get(ReferenceIndex references, TextLeafBlock leaf)
    {
        if (Cache.TryGetValue(leaf, out var entry))
        {
            if (ReferenceEquals(entry.References, references))
            {
                return entry.Tree;
            }

            if (entry.Tree.IsValidFor(references.IsLinkDefined))
            {
                Cache.AddOrUpdate(leaf, entry with { References = references });
                return entry.Tree;
            }
        }

        var tree = MarkdownInlines.Parser.Parse(leaf.Text, leaf.InlineSyntax, references.IsLinkDefined);
        Cache.AddOrUpdate(leaf, new Entry(tree, references));
        return tree;
    }

    /// <summary>
    /// Whether the tree is one image and nothing else but spaces (a link around the image counts as the
    /// image): such a paragraph shows its image as a block (10).
    /// </summary>
    public static bool IsImageOnly(InlineTree tree)
    {
        int images = 0;
        foreach (var node in tree.Nodes)
        {
            switch (node.Kind)
            {
                case InlineKind.Image:
                case InlineKind.Link when node.Children.Length == 1 && node.Children[0].Kind == InlineKind.Image:
                    images++;
                    break;
                case InlineKind.Text when string.IsNullOrWhiteSpace(tree.Source[node.Range.Start..node.Range.End]):
                    break;
                default:
                    return false;
            }
        }

        return images == 1;
    }

    private sealed record Entry(InlineTree Tree, ReferenceIndex References);
}
