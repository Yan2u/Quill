using System.Collections.Immutable;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Documents;

/// <summary>
/// The root of the block tree. Its <see cref="Block.Revision"/> is the global document revision.
/// </summary>
public sealed record Document : ContainerBlock
{
    public override BlockKind Kind => BlockKind.Document;

    /// <summary>Line endings, encoding and byte order mark of the file, restored on save.</summary>
    public DocumentFormat Format { get; init; } = DocumentFormat.Default;
}

/// <summary>
/// A block quote.
/// </summary>
public sealed record BlockQuote : ContainerBlock
{
    public override BlockKind Kind => BlockKind.BlockQuote;
}

/// <summary>
/// List syntax as written.
/// </summary>
/// <param name="MarkerChar">The bullet ('-', '*', '+') or delimiter ('.', ')') of the first item, used for new items.</param>
/// <param name="WasLoose">
/// Whether the list was loose when imported by the rule of <see cref="ListRules.IsLoose"/>: loose in the
/// source, or holding a headless item. It stays loose (Q27); separators are rewritten only when the
/// current looseness differs from this.
/// </param>
/// <param name="SeparateFrom">
/// The previous sibling list of the same type and marker that the source already kept apart from this
/// one (CommonMark can end a list without a marker change). While that list still precedes this one,
/// the marker is not switched (Q25).
/// </param>
public sealed record ListSyntax(char MarkerChar, bool WasLoose, BlockId? SeparateFrom = null);

/// <summary>
/// A list. Its children are <see cref="ListItem"/>s.
/// </summary>
public sealed record ListBlock : ContainerBlock
{
    public override BlockKind Kind => BlockKind.List;

    public required bool IsOrdered { get; init; }

    public ListSyntax? Syntax { get; init; }

    /// <summary>The bullet or delimiter character for generated items.</summary>
    public char MarkerChar => Syntax?.MarkerChar ?? (IsOrdered ? '.' : '-');
}

/// <summary>
/// Task state of a list item.
/// </summary>
public enum TaskState
{
    None,
    Unchecked,
    Checked,
}

/// <summary>
/// A list item. Its first child is the item's line (shown with the bullet or number); later children
/// are headless list items or sub-lists (02-document-model.md).
/// </summary>
public sealed record ListItem : ContainerBlock
{
    public override BlockKind Kind => BlockKind.ListItem;

    public TaskState Task { get; init; }

    /// <summary>The number written in the source for items of ordered lists; null otherwise.</summary>
    public int? Number { get; init; }

    public ListItemSyntax? Syntax { get; init; }
}

/// <summary>
/// Column alignment of a table.
/// </summary>
public enum TableAlignment
{
    None,
    Left,
    Center,
    Right,
}

/// <summary>
/// A table. Its first row is the header row; every row has one cell per column.
/// </summary>
public sealed record Table : ContainerBlock
{
    public override BlockKind Kind => BlockKind.Table;

    public required ImmutableArray<TableAlignment> Alignments { get; init; }

    public TableSyntax? Syntax { get; init; }

    public int ColumnCount => Alignments.Length;
}

/// <summary>
/// A table row. Its children are <see cref="TableCell"/>s.
/// </summary>
public sealed record TableRow : ContainerBlock
{
    public override BlockKind Kind => BlockKind.TableRow;

    public TableRowSyntax? Syntax { get; init; }
}

/// <summary>
/// A code block (Q91): the code (<see cref="CodeBlock"/>) and the language (<see cref="CodeLanguage"/>),
/// in that order, so that Down from the last line of code reaches the language and Up from below reaches
/// the language before the code. The fence syntax (or the indentation) belongs to the container.
/// </summary>
public sealed record CodeFence : ContainerBlock
{
    public override BlockKind Kind => BlockKind.CodeFence;

    public CodeBlock Code => (CodeBlock)Children[0];

    public CodeLanguage Language => (CodeLanguage)Children[1];

    /// <summary>The info string: language and attributes (the text of the language slot).</summary>
    public string Info => Language.Text;

    public CodeBlockSyntax? Syntax { get; init; }

    /// <summary>A new code block with new IDs for the container and both slots.</summary>
    public static CodeFence Create(Func<BlockId> newId, string text, string info, CodeBlockSyntax? syntax = null) => new()
    {
        Id = newId(),
        Syntax = syntax,
        Children = [new CodeBlock { Id = newId(), Text = text }, new CodeLanguage { Id = newId(), Text = info }],
    };

    /// <summary>The code block a code or language slot belongs to, or null for other blocks.</summary>
    public static CodeFence? Of(Document document, Block block) =>
        block is CodeBlock or CodeLanguage && DocumentTree.Locate(document, block.Id).Parent is CodeFence fence ? fence : null;
}

/// <summary>
/// A footnote definition: a name slot and a single-line content slot (Q30). Further blocks of a
/// multi-paragraph footnote follow the slots as children; they keep the footnote's indentation on
/// export (02-document-model.md default).
/// </summary>
public sealed record FootnoteDefinition : ContainerBlock
{
    public override BlockKind Kind => BlockKind.FootnoteDefinition;

    public DefinitionSlot Name => (DefinitionSlot)Children[0];

    public DefinitionSlot Content => (DefinitionSlot)Children[1];

    public FootnoteDefinitionSyntax? Syntax { get; init; }
}

/// <summary>
/// A link reference definition with label, destination and title slots (Q30).
/// </summary>
public sealed record LinkReferenceDefinition : ContainerBlock
{
    public override BlockKind Kind => BlockKind.LinkReferenceDefinition;

    public DefinitionSlot Label => (DefinitionSlot)Children[0];

    public DefinitionSlot Destination => (DefinitionSlot)Children[1];

    public DefinitionSlot Title => (DefinitionSlot)Children[2];

    public LinkReferenceDefinitionSyntax? Syntax { get; init; }
}
