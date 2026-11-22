using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Markdown.Export;

namespace Quill.Editor.Markdown;

/// <summary>
/// Writes the document model as Markdown with Quill's own serializer (Q1): recorded syntax is replayed
/// so that unchanged content is reproduced byte for byte; syntax is generated only for new or
/// structurally changed parts. No implicit normalization (06-markdown-io.md).
/// </summary>
public static class MarkdownSerializer
{
    /// <summary>
    /// Serializes the whole document with its original line break style restored. Counts as one
    /// full-document serialization.
    /// </summary>
    public static string Serialize(Document document, EditorOptions? options = null)
    {
        return LineEndings.Apply(SerializeNormalized(document, options), document.Format.LineEnding);
    }

    /// <summary>Serializes the whole document with "\n" line breaks (source mode, clipboard).</summary>
    public static string SerializeNormalized(Document document, EditorOptions? options = null)
    {
        OperationCounters.Record(CountedOperation.FullDocumentSerialization);

        // A source document is its text (11-source-mode.md): saved as written, without an import.
        return Editing.SourceText.IsSource(document) ? Editing.SourceText.ToText(document) : BlockWriter.Write(document, options ?? EditorOptions.Default);
    }

    /// <summary>
    /// Serializes the whole document with "\n" line breaks and reports where each block starts in the
    /// output: after the prefixes of its ancestors, so a list item starts at its marker.
    /// </summary>
    public static string SerializeNormalized(Document document, out IReadOnlyDictionary<BlockId, int> blockStarts, EditorOptions? options = null)
    {
        OperationCounters.Record(CountedOperation.FullDocumentSerialization);
        var starts = new Dictionary<BlockId, int>();
        string text = BlockWriter.Write(document, options ?? EditorOptions.Default, starts);
        blockStarts = starts;
        return text;
    }

    /// <summary>
    /// Serializes blocks as a stand-alone Markdown fragment with "\n" line breaks and no trailing line
    /// break (clipboard). Not a full-document serialization.
    /// </summary>
    public static string SerializeFragment(IReadOnlyList<Block> blocks, EditorOptions? options = null)
    {
        return BlockWriter.WriteFragment(blocks, options ?? EditorOptions.Default);
    }

    /// <summary>Serializes the document to file bytes: line breaks, encoding and byte order mark restored.</summary>
    public static byte[] SerializeFile(Document document, EditorOptions? options = null) => SerializeFile(document, options, out _);

    /// <summary>
    /// Serializes the document to file bytes; <paramref name="format"/> is the format written: UTF-8
    /// instead of a legacy code page that cannot hold the text (M6.4).
    /// </summary>
    public static byte[] SerializeFile(Document document, EditorOptions? options, out DocumentFormat format)
    {
        (var bytes, format) = MarkdownFileCodec.Encode(Serialize(document, options), document.Format);
        return bytes;
    }
}
