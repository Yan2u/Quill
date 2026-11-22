using Quill.Editor.Documents;

namespace Quill.Editor.Editing;

/// <summary>
/// Source documents (11-source-mode.md): Markdown text as one <see cref="SourceLine"/> per line, and back.
/// The document keeps the format (line breaks, encoding) of the file the text belongs to.
/// </summary>
public static class SourceText
{
    /// <summary>The source document of a text: one line block per line ("\n" separated), at least one.</summary>
    public static Document ToDocument(string text, DocumentFormat format, IBlockIdSource ids)
    {
        var lines = text.Split('\n');
        return new Document
        {
            Id = ids.NextId(),
            Format = format,
            Children = [.. lines.Select(line => (Block)new SourceLine { Id = ids.NextId(), Text = line })],
        };
    }

    /// <summary>The text of a source document: its lines joined by "\n".</summary>
    public static string ToText(Document document) =>
        string.Join('\n', document.Children.Select(static block => ((LeafBlock)block).Text));

    /// <summary>Whether a document is a source document.</summary>
    public static bool IsSource(Document document) => document.Children.Count > 0 && document.Children[0] is SourceLine;
}
