using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Documents;

/// <summary>
/// A paragraph. A text of only spaces and tabs (a CommonMark blank line) is an empty paragraph, which
/// stands for one extra blank line in the source (02-document-model.md).
/// </summary>
public sealed record Paragraph : TextLeafBlock
{
    public override BlockKind Kind => BlockKind.Paragraph;

    public bool IsEmpty => Text.AsSpan().TrimStart(" \t").IsEmpty;
}

/// <summary>
/// A heading of level 1 to 6. The text excludes the '#' markers and the Setext underline.
/// </summary>
public sealed record Heading : TextLeafBlock
{
    public override BlockKind Kind => BlockKind.Heading;

    public required int Level { get; init; }

    public HeadingSyntax? Syntax { get; init; }
}

/// <summary>
/// A table cell. The text excludes the surrounding pipes and padding, and stores '|' as "\|".
/// </summary>
public sealed record TableCell : TextLeafBlock
{
    public override BlockKind Kind => BlockKind.TableCell;
}

/// <summary>
/// The language of a code block (Q91): the info string after the opening fence, as editable text of the
/// document. A single line of plain text; shown outside the code block at its bottom right while the
/// block is focused.
/// </summary>
public sealed record CodeLanguage : TextLeafBlock
{
    public override BlockKind Kind => BlockKind.CodeLanguage;

    public override InlineSyntaxSet InlineSyntax => InlineSyntaxSet.PlainText;
}

/// <summary>
/// The slots of definition blocks (10-elements.md).
/// </summary>
public enum DefinitionSlotKind
{
    FootnoteName,
    FootnoteContent,
    LinkLabel,
    LinkDestination,
    LinkTitle,
}

/// <summary>
/// An editable slot of a definition block. Brackets, colons and quotes around it are decoration drawn
/// by the view. Only footnote content is parsed for (some) inline syntax; the other slots are plain text.
/// </summary>
public sealed record DefinitionSlot : TextLeafBlock
{
    public override BlockKind Kind => BlockKind.DefinitionSlot;

    public required DefinitionSlotKind SlotKind { get; init; }

    public override InlineSyntaxSet InlineSyntax =>
        SlotKind == DefinitionSlotKind.FootnoteContent ? InlineSyntaxSet.FootnoteContent : InlineSyntaxSet.PlainText;
}
