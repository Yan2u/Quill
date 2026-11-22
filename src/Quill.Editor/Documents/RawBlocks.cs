using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Documents;

/// <summary>
/// The code of a code block, including diagrams such as Mermaid: the first child of a
/// <see cref="CodeFence"/>, which holds the language and the fence syntax. The text is the code without
/// fences or indentation.
/// </summary>
public sealed record CodeBlock : RawTextBlock
{
    public override BlockKind Kind => BlockKind.CodeBlock;
}

/// <summary>
/// A display math block delimited by "$$" lines. The text is the TeX source.
/// </summary>
public sealed record MathBlock : RawTextBlock
{
    public override BlockKind Kind => BlockKind.MathBlock;

    public FenceSyntax? Syntax { get; init; }
}

/// <summary>
/// An HTML block, kept and shown as source (Q31).
/// </summary>
public sealed record HtmlBlock : RawTextBlock
{
    public override BlockKind Kind => BlockKind.HtmlBlock;
}

/// <summary>
/// YAML front matter at the start of the document. The text excludes the "---" fences.
/// </summary>
public sealed record FrontMatter : RawTextBlock
{
    public override BlockKind Kind => BlockKind.FrontMatter;

    public FenceSyntax? Syntax { get; init; }
}

/// <summary>
/// One line of Markdown source, without its line break: the only kind of block of a source document
/// (11-source-mode.md). The source text is the lines joined by "\n".
/// </summary>
public sealed record SourceLine : RawTextBlock
{
    public override BlockKind Kind => BlockKind.SourceLine;
}

/// <summary>
/// A thematic break ("---").
/// </summary>
public sealed record ThematicBreak : AtomicBlock
{
    public override BlockKind Kind => BlockKind.ThematicBreak;
}

/// <summary>
/// The table of contents marker ("[toc]").
/// </summary>
public sealed record TocBlock : AtomicBlock
{
    public override BlockKind Kind => BlockKind.Toc;
}
