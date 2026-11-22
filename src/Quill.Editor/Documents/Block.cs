using System.Collections.Immutable;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Documents;

/// <summary>
/// A node of the document tree. Nodes are immutable; edits copy the path from the root and share
/// untouched subtrees (02-document-model.md).
/// </summary>
public abstract record Block
{
    public required BlockId Id { get; init; }

    /// <summary>The document revision in which this node last changed.</summary>
    public long Revision { get; init; }

    public abstract BlockKind Kind { get; }

    /// <summary>
    /// The blank lines between the previous sibling and this block, as written; null when not recorded
    /// (generated on export). Export-only syntax metadata.
    /// </summary>
    public BlockSeparator? SeparatorBefore { get; init; }
}

/// <summary>
/// A block whose content is child blocks.
/// </summary>
public abstract record ContainerBlock : Block
{
    public required ImmutableList<Block> Children { get; init; }
}

/// <summary>
/// A block whose content is its own source text (the leaf source). Line breaks are "\n".
/// </summary>
public abstract record LeafBlock : Block
{
    public required string Text { get; init; }

    /// <summary>
    /// Prefixes of the physical lines of <see cref="Text"/> as written: entry k belongs to the line that
    /// starts after the k-th "\n"; entry 0 to the first line, when the text starts a line. Lines without
    /// an entry get generated prefixes. Export-only syntax metadata.
    /// </summary>
    public ImmutableArray<LinePrefix> LinePrefixes { get; init; } = ImmutableArray<LinePrefix>.Empty;

    public LinePrefix PrefixAt(int line) => line < LinePrefixes.Length ? LinePrefixes[line] : LinePrefix.None;
}

/// <summary>
/// A leaf whose source contains inline Markdown: paragraphs, headings, table cells and definition slots.
/// </summary>
public abstract record TextLeafBlock : LeafBlock
{
    /// <summary>The inline syntax the leaf source is parsed with (Q44).</summary>
    public virtual InlineSyntaxSet InlineSyntax => InlineSyntaxSet.Full;
}

/// <summary>
/// Which inline syntax a text leaf's source is parsed with (06-markdown-io.md, Q44).
/// </summary>
public enum InlineSyntaxSet
{
    /// <summary>All supported inline syntax: paragraphs, headings, table cells.</summary>
    Full,

    /// <summary>Bold, italic, inline math, superscript, subscript and inline code: footnote content.</summary>
    FootnoteContent,

    /// <summary>No inline syntax; markers are ordinary text: footnote names and link definition slots.</summary>
    PlainText,
}

/// <summary>
/// A leaf whose text is not parsed for inlines: code, math, HTML and front matter.
/// </summary>
public abstract record RawTextBlock : LeafBlock;

/// <summary>
/// A block without content, shown as a whole: thematic breaks and the table of contents.
/// </summary>
public abstract record AtomicBlock : Block
{
    /// <summary>The block's line as written. Export-only syntax metadata.</summary>
    public SyntaxLine? Syntax { get; init; }
}
