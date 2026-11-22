using Quill.Editor.Inlines;
using Quill.Editor.Markdown.Inlines;

namespace Quill.Editor.Markdown;

/// <summary>
/// The inline parser of Markdown I/O (06-markdown-io.md).
/// </summary>
public static class MarkdownInlines
{
    /// <summary>The Markdig-based parser. Thread-safe.</summary>
    public static IInlineParser Parser => MarkdigInlineParser.Instance;
}
