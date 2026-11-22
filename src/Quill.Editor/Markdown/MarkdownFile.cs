using Quill.Editor.Documents;

namespace Quill.Editor.Markdown;

/// <summary>
/// Opening and saving Markdown files (06): the line break style, encoding and byte order mark of the
/// file are recorded on open and restored on save. Saving serializes an immutable snapshot on a
/// background thread (09, rule 5) and replaces the file only once the new content is fully written.
/// </summary>
public static class MarkdownFile
{
    private const string TemporarySuffix = ".quill-save";

    /// <summary>Reads and imports a file, detecting its encoding or with <paramref name="encoding"/> (M6.4).</summary>
    public static async Task<MarkdownImportResult> OpenAsync(string path, IBlockIdSource ids, CancellationToken cancellation = default, TextEncodingChoice? encoding = null)
    {
        var bytes = await File.ReadAllBytesAsync(path, cancellation).ConfigureAwait(false);
        return MarkdownImporter.ImportFile(bytes, ids, encoding);
    }

    /// <summary>
    /// Serializes <paramref name="document"/> and writes it to <paramref name="path"/>. The bytes go to a
    /// temporary file next to the target first, which then replaces it, so a failure never leaves a
    /// half-written file behind.
    /// </summary>
    /// <returns>The format written: the document's, or UTF-8 when its legacy code page cannot hold the text.</returns>
    public static Task<DocumentFormat> SaveAsync(Document document, string path, EditorOptions? options = null, CancellationToken cancellation = default) =>
        Task.Run(
            async () =>
            {
                var bytes = MarkdownSerializer.SerializeFile(document, options, out var written);
                string full = Path.GetFullPath(path);
                string temporary = full + TemporarySuffix;
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellation).ConfigureAwait(false);
                    File.Move(temporary, full, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary))
                    {
                        File.Delete(temporary);
                    }
                }

                return written;
            },
            cancellation);
}
