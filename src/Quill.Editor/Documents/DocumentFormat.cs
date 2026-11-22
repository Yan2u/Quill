namespace Quill.Editor.Documents;

/// <summary>
/// Line break style of a file.
/// </summary>
public enum LineEnding
{
    Lf,
    CrLf,
    Cr,
}

/// <summary>
/// Text encodings recognized when opening a file.
/// </summary>
public enum TextEncodingKind
{
    Utf8,
    Utf16LittleEndian,
    Utf16BigEndian,

    /// <summary>A Windows code page such as GB18030 (<see cref="DocumentFormat.CodePage"/>).</summary>
    Legacy,
}

/// <summary>
/// How the document's text is laid out in its file. In memory every line break is "\n"; the original
/// style is restored on save (02-document-model.md).
/// </summary>
public sealed record DocumentFormat
{
    /// <summary>LF, UTF-8 without byte order mark, ending with a line break: what is assumed when nothing is known.</summary>
    public static DocumentFormat Default { get; } = new();

    /// <summary>
    /// The format of a new document (02, Q46, Q103): UTF-8 without byte order mark, ending with a line
    /// break, with the line break of the settings (CRLF by default).
    /// </summary>
    public static DocumentFormat ForNewDocument(LineEnding lineEnding) => Default with { LineEnding = lineEnding };

    /// <summary>The line break written on save; the first line break of the source when imported.</summary>
    public LineEnding LineEnding { get; init; } = LineEnding.Lf;

    /// <summary>The source mixed several line break styles; saving writes <see cref="LineEnding"/> everywhere.</summary>
    public bool HasMixedLineEndings { get; init; }

    public TextEncodingKind Encoding { get; init; } = TextEncodingKind.Utf8;

    public bool HasByteOrderMark { get; init; }

    /// <summary>The Windows code page when <see cref="Encoding"/> is <see cref="TextEncodingKind.Legacy"/>, else 0.</summary>
    public int CodePage { get; init; }

    /// <summary>Whether the text ends with a line break.</summary>
    public bool EndsWithLineBreak { get; init; } = true;
}
