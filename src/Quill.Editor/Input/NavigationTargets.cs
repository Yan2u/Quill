using Quill.Editor.Documents;
using Quill.Editor.Inlines;
using Quill.Editor.State;

namespace Quill.Editor.Input;

/// <summary>What Ctrl+click on an element does (07, Q38).</summary>
public abstract record NavigationTarget;

/// <summary>A footnote reference: go to its definition.</summary>
public sealed record FootnoteTarget(string Label) : NavigationTarget;

/// <summary>
/// A link, autolink or bare URL: <c>#fragment</c> goes to a heading in the document; anything else is
/// opened by the host (a browser, or a local Markdown file in Quill), relative to the document's folder.
/// </summary>
public sealed record LinkTarget(string Destination) : NavigationTarget
{
    public bool IsAnchor => Destination.StartsWith('#');
}

/// <summary>Finds the element a position touches that Ctrl+click navigates from.</summary>
public static class NavigationTargets
{
    /// <summary>The innermost link or footnote reference touching the position, or null.</summary>
    public static NavigationTarget? At(EditorState state, Position position)
    {
        var document = state.Document;
        if (position is not TextPosition text || DocumentTree.TryFind(document, text.Block) is not TextLeafBlock leaf)
        {
            return null;
        }

        var references = ReferenceIndex.Of(document);
        var nodes = InlineTrees.Get(references, leaf).NodesTouching(text.Offset);
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            switch (node.Kind)
            {
                case InlineKind.FootnoteReference:
                    return new FootnoteTarget(node.Label ?? string.Empty);
                case InlineKind.Autolink or InlineKind.Url when node.Value is { } url:
                    return new LinkTarget(node.IsEmail && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ? "mailto:" + url : url);
                case InlineKind.Link when node.Value is { Length: > 0 } url:
                    return new LinkTarget(url);
                case InlineKind.Link when node.Label is { } label
                    && references.ResolveLink(document, LinkLabel.Normalize(label)) is { } id
                    && DocumentTree.TryFind(document, id) is LinkReferenceDefinition definition:
                    return new LinkTarget(definition.Destination.Text.Trim('<', '>'));
            }
        }

        return null;
    }
}
