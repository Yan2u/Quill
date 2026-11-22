using Quill.Editor.Diagnostics;
using Quill.Editor.Documents;
using Quill.Editor.Markdown.Import;
using Quill.Editor.Markdown.Parsing;

namespace Quill.Editor.Markdown;

/// <summary>
/// The result of importing Markdown. <see cref="Diagnostics"/> lists source regions that could not be
/// mapped to a supported block and were kept as raw text; it is empty for supported input.
/// </summary>
public sealed record MarkdownImportResult(Document Document, IReadOnlyList<string> Diagnostics);

/// <summary>
/// Imports Markdown into the document model (06-markdown-io.md). Markdig provides structure and
/// positions; leaf sources and syntax metadata are sliced from the original text, so that
/// <c>Serialize(Import(m)) == m</c> for unedited documents.
/// </summary>
public static class MarkdownImporter
{
    /// <summary>Imports a whole document. Counts as one full-document parse.</summary>
    public static MarkdownImportResult Import(string markdown, IBlockIdSource ids)
    {
        OperationCounters.Record(CountedOperation.FullDocumentParse);
        var (normalized, lineEnding, mixed) = LineEndings.Normalize(markdown);
        var source = new SourceText(normalized);
        var documentId = ids.NextId();
        var parsed = Markdig.Markdown.Parse(normalized, MarkdigPipelines.Import);
        var converter = new BlockConverter(source, ids);
        var document = new Document
        {
            Id = documentId,
            Children = converter.ConvertDocument(parsed),
            Format = new DocumentFormat
            {
                LineEnding = lineEnding,
                HasMixedLineEndings = mixed,
                EndsWithLineBreak = source.EndsWithLineBreak,
            },
        };
        return new MarkdownImportResult(document, converter.Diagnostics);
    }

    /// <summary>Decodes a file (detecting its encoding, or with <paramref name="encoding"/>) and imports it, recording the encoding.</summary>
    public static MarkdownImportResult ImportFile(ReadOnlySpan<byte> bytes, IBlockIdSource ids, TextEncodingChoice? encoding = null)
    {
        var decoded = MarkdownFileCodec.Decode(bytes, encoding);
        var result = Import(decoded.Text, ids);
        var format = result.Document.Format with { Encoding = decoded.Encoding, HasByteOrderMark = decoded.HasByteOrderMark, CodePage = decoded.CodePage };
        return result with { Document = result.Document with { Format = format } };
    }

    /// <summary>
    /// Imports a Markdown fragment as blocks: pasted text, or a paragraph re-parsed when it is
    /// committed. Not a full-document parse. Line breaks are normalized to "\n".
    /// </summary>
    public static IReadOnlyList<Block> ImportFragment(string markdown, IBlockIdSource ids)
    {
        var (normalized, _, _) = LineEndings.Normalize(markdown);
        var parsed = Markdig.Markdown.Parse(normalized, MarkdigPipelines.Import);
        return new BlockConverter(new SourceText(normalized), ids).ConvertDocument(parsed);
    }
}
