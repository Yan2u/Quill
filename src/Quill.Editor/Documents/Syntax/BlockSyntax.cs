using System.Collections.Immutable;

namespace Quill.Editor.Documents.Syntax;

/// <summary>
/// How a heading was written.
/// </summary>
public enum HeadingStyle
{
    Atx,
    Setext,
}

/// <summary>
/// Heading syntax as written. A change of level regenerates the markers.
/// </summary>
public sealed record HeadingSyntax
{
    public required HeadingStyle Style { get; init; }

    /// <summary>The level the markers encode.</summary>
    public required int Level { get; init; }

    /// <summary>ATX: indentation, the '#' run and the whitespace before the content. Setext: whitespace before the content.</summary>
    public string Opening { get; init; } = string.Empty;

    /// <summary>ATX: the optional closing sequence and trailing whitespace. Setext: whitespace after the content.</summary>
    public string Closing { get; init; } = string.Empty;

    /// <summary>Setext: the underline line.</summary>
    public SyntaxLine? Underline { get; init; }

    /// <summary>
    /// Setext: the heading text as read. While the text is unchanged it is written as Setext again;
    /// edited text is checked first, since text that starts a block (such as "$$") would not read
    /// back as a heading.
    /// </summary>
    public string? SetextText { get; init; }
}

/// <summary>
/// The fences of a fenced code block, a math block or front matter.
/// </summary>
public sealed record FenceSyntax
{
    /// <summary>The opening line: indentation, fence characters and, for code, the info string.</summary>
    public required SyntaxLine Opening { get; init; }

    /// <summary>The closing line; null when the source left the fence unclosed.</summary>
    public SyntaxLine? Closing { get; init; }

    public required char FenceChar { get; init; }

    public required int FenceLength { get; init; }

    /// <summary>
    /// Columns of indentation before the opening fence. Up to this many columns are removed from each
    /// content line; the removed text is kept as a prefix segment owned by the block itself.
    /// </summary>
    public int IndentColumns { get; init; }

    /// <summary>The info string when imported. A different current info regenerates the opening line.</summary>
    public string Info { get; init; } = string.Empty;

    /// <summary>
    /// True when the fences enclose no line at all ("```" directly followed by "```"). An empty text
    /// otherwise means a single empty line.
    /// </summary>
    public bool HasNoContentLines { get; init; }

    /// <summary>
    /// For a block the source left unclosed: its text then. It is written unclosed again only while its
    /// text is unchanged; edited code gets a closing fence (its last lines could otherwise change meaning).
    /// </summary>
    public string? UnclosedText { get; init; }
}

/// <summary>
/// How a code block was written.
/// </summary>
public enum CodeBlockStyle
{
    Fenced,
    Indented,
}

public sealed record CodeBlockSyntax
{
    public required CodeBlockStyle Style { get; init; }

    /// <summary>Set when <see cref="Style"/> is <see cref="CodeBlockStyle.Fenced"/>.</summary>
    public FenceSyntax? Fence { get; init; }
}

/// <summary>
/// The opening of a list item as written.
/// </summary>
public sealed record ListItemSyntax
{
    /// <summary>Whitespace before the marker.</summary>
    public required string Indent { get; init; }

    /// <summary>The marker as written: "-", "*", "+", "1.", "007)".</summary>
    public required string Marker { get; init; }

    /// <summary>Whitespace after the marker, up to the content or to the end of a marker-only line.</summary>
    public required string Spacing { get; init; }

    /// <summary>The marker line holds no content; the first child starts on the next line.</summary>
    public bool OpensWithBlankLine { get; init; }

    /// <summary>Prefixes of the marker-only line, when <see cref="OpensWithBlankLine"/> is set.</summary>
    public LinePrefix OpeningLinePrefix { get; init; }

    /// <summary>The task marker with the whitespace that follows it, such as "[ ] "; null for plain items.</summary>
    public string? TaskMarker { get; init; }
}

/// <summary>
/// The delimiter row of a table as written.
/// </summary>
public sealed record TableSyntax
{
    public required SyntaxLine DelimiterRow { get; init; }

    /// <summary>The alignments the delimiter row encodes; a change regenerates the row.</summary>
    public required ImmutableArray<TableAlignment> Alignments { get; init; }
}

/// <summary>
/// The text of a table row around its cells. Reused only while the row still holds the cells written
/// in the source, in the same order (cells padded in by the parser follow them and must stay empty).
/// </summary>
public sealed record TableRowSyntax
{
    public required LinePrefix Prefix { get; init; }

    /// <summary>Text before, between and after the written cells: pipes and padding.</summary>
    public required ImmutableArray<string> Gaps { get; init; }

    /// <summary>IDs of the cells written in the source; <c>Gaps.Length == Cells.Length + 1</c>.</summary>
    public required ImmutableArray<BlockId> Cells { get; init; }
}

/// <summary>
/// The text around the slots of a footnote definition: <c>Opening + name + Separator + content</c>.
/// </summary>
public sealed record FootnoteDefinitionSyntax
{
    public required LinePrefix Prefix { get; init; }

    /// <summary>Indentation and "[^".</summary>
    public required string Opening { get; init; }

    /// <summary>"]:" and the whitespace before the content.</summary>
    public required string Separator { get; init; }
}

/// <summary>
/// The text around the slots of a link reference definition:
/// <c>Opening + label + AfterLabel + destination + AfterDestination + title + AfterTitle</c>.
/// </summary>
public sealed record LinkReferenceDefinitionSyntax
{
    /// <summary>Indentation and "[". Its first prefix belongs to the definition's first line.</summary>
    public required SyntaxText Opening { get; init; }

    /// <summary>"]:", whitespace (possibly a line break) and "&lt;" for a pointy destination.</summary>
    public required SyntaxText AfterLabel { get; init; }

    /// <summary>"&gt;" for a pointy destination, whitespace and the title's opening delimiter.</summary>
    public required SyntaxText AfterDestination { get; init; }

    /// <summary>The title's closing delimiter and trailing whitespace.</summary>
    public required SyntaxText AfterTitle { get; init; }

    /// <summary>Whether the source had a title; a different current state regenerates the title syntax.</summary>
    public required bool HasTitle { get; init; }

    /// <summary>Whether the destination is written in angle brackets.</summary>
    public bool IsPointy => AfterLabel.Text.EndsWith('<');
}
