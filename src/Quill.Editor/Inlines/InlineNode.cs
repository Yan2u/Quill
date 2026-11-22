using System.Collections.Immutable;

namespace Quill.Editor.Inlines;

/// <summary>
/// A node of a leaf's inline syntax tree (06-markdown-io.md). The source from <see cref="Range"/>'s
/// start to <see cref="Content"/>'s start is the opening syntax, the source between the ends the
/// closing syntax. When <see cref="Children"/> is not empty they tile <see cref="Content"/> exactly;
/// otherwise the content is raw text.
/// </summary>
public sealed class InlineNode
{
    public InlineNode(InlineKind kind, TextRange range, TextRange content, ImmutableArray<InlineNode> children = default)
    {
        Kind = kind;
        Range = range;
        Content = content;
        Children = children.IsDefault ? [] : children;
    }

    public InlineKind Kind { get; }

    /// <summary>The whole node in the leaf source.</summary>
    public TextRange Range { get; }

    /// <summary>The content between the opening and closing syntax.</summary>
    public TextRange Content { get; }

    public ImmutableArray<InlineNode> Children { get; }

    public TextRange Opening => new(Range.Start, Content.Start);

    public TextRange Closing => new(Content.End, Range.End);

    /// <summary>
    /// Kind-specific text: the URL of links, images, autolinks and URLs; the emoji of an emoji; the
    /// character of an entity; the lowercase tag name of an HTML span.
    /// </summary>
    public string? Value { get; init; }

    /// <summary>The title of an inline link or image.</summary>
    public string? Title { get; init; }

    /// <summary>The label of a reference link or image (as written), or of a footnote reference.</summary>
    public string? Label { get; init; }

    public LinkForm LinkForm { get; init; }

    /// <summary>The destination of an inline link or image in the source, angle brackets included.</summary>
    public TextRange? DestinationRange { get; init; }

    /// <summary>The title of an inline link or image in the source, delimiters included.</summary>
    public TextRange? TitleRange { get; init; }

    /// <summary>Whether an autolink is an e-mail address.</summary>
    public bool IsEmail { get; init; }

    public override string ToString() => $"{Kind}{Range}";
}
