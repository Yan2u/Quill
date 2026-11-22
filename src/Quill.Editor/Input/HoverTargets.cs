using System.Globalization;
using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>
/// The hover target (03: <c>Interaction.HoverTarget</c>): the inline element under the pointer that
/// shows a flyout (10, Q29): a footnote reference, or a link with a title. Its form is
/// <c>b{block}:{start}-{end}</c>, the element's source range in its leaf, so the projection finds
/// it again in the current document and drops it when the element is gone.
/// </summary>
public static class HoverTargets
{
    /// <summary>The target at a position, or null when the element there shows no flyout.</summary>
    public static string? At(EditorState state, Position position)
    {
        if (position is not TextPosition text || DocumentTree.TryFind(state.Document, text.Block) is not TextLeafBlock leaf)
        {
            return null;
        }

        var nodes = InlineTrees.Get(ReferenceIndex.Of(state.Document), leaf).NodesTouching(text.Offset);
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            if (Shows(nodes[i], state.Document) is not null)
            {
                return Format(leaf.Id, nodes[i].Range);
            }
        }

        return null;
    }

    public static string Format(BlockId block, TextRange range) => $"b{block.Value}:{range.Start}-{range.End}";

    public static bool TryParse(string? target, out BlockId block, out TextRange range)
    {
        block = default;
        range = default;
        if (target is null || !target.StartsWith('b'))
        {
            return false;
        }

        int colon = target.IndexOf(':');
        int dash = target.IndexOf('-', Math.Max(colon, 0));
        if (colon < 0 || dash < 0
            || !long.TryParse(target.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out long id)
            || !int.TryParse(target.AsSpan(colon + 1, dash - colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out int start)
            || !int.TryParse(target.AsSpan(dash + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int end))
        {
            return false;
        }

        block = new BlockId(id);
        range = new TextRange(start, end);
        return true;
    }

    /// <summary>What an element's flyout says, and whether it is a warning; null when it shows none.</summary>
    internal static (string Text, bool IsWarning)? Shows(InlineNode node, Document document)
    {
        switch (node.Kind)
        {
            case InlineKind.FootnoteReference:
                var references = ReferenceIndex.Of(document);
                string label = node.Label ?? string.Empty;
                return references.ResolveFootnote(document, LinkLabel.Normalize(label)) is { } id && DocumentTree.TryFind(document, id) is FootnoteDefinition definition
                    ? (FootnoteText(definition, references), false)
                    : ($"Footnote [^{label}] is not defined.", true);
            case InlineKind.Link when !string.IsNullOrEmpty(node.Title):
                return (node.Title!, false);
            case InlineKind.Link when node.Label is { } reference
                && ReferenceIndex.Of(document).ResolveLink(document, LinkLabel.Normalize(reference)) is { } linkId
                && DocumentTree.TryFind(document, linkId) is LinkReferenceDefinition link
                && link.Title.Text.Length > 0:
                return (link.Title.Text, false);
            default:
                return null;
        }
    }

    private static string FootnoteText(FootnoteDefinition definition, ReferenceIndex references)
    {
        var parts = definition.Children.Skip(1).OfType<LeafBlock>().Select(leaf => Projection.PlainText.Of(leaf, references));
        return string.Join("\n", parts).Trim();
    }
}
