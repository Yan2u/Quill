namespace Quill.Editor.Documents;

/// <summary>
/// The kinds of blocks in the document tree (02-document-model.md).
/// </summary>
public enum BlockKind
{
    Document,

    // Containers.
    BlockQuote,
    List,
    ListItem,
    Table,
    TableRow,
    CodeFence,

    // Text leaves: inline Markdown source.
    Paragraph,
    Heading,
    TableCell,
    DefinitionSlot,
    CodeLanguage,

    // Raw text leaves: no inline parsing.
    CodeBlock,
    MathBlock,
    HtmlBlock,
    FrontMatter,

    // A line of Markdown source in the source mode (11-source-mode.md).
    SourceLine,

    // Definition blocks: containers of slots.
    FootnoteDefinition,
    LinkReferenceDefinition,

    // Atomic blocks: no content.
    ThematicBreak,
    Toc,
}
