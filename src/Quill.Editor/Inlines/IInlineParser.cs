using Quill.Editor.Documents;

namespace Quill.Editor.Inlines;

/// <summary>
/// Parses a leaf source into an <see cref="InlineTree"/> (Q2). The Markdig-based implementation lives
/// in Markdown I/O; nothing else sees Markdig.
/// </summary>
public interface IInlineParser
{
    /// <param name="source">The leaf source.</param>
    /// <param name="syntax">The inline syntax set of the leaf (Q44).</param>
    /// <param name="isLinkDefined">
    /// Whether a link reference definition with the given normalized label (<see cref="LinkLabel.Normalize"/>)
    /// exists; null when there are none.
    /// </param>
    InlineTree Parse(string source, InlineSyntaxSet syntax, Func<string, bool>? isLinkDefined = null);
}
