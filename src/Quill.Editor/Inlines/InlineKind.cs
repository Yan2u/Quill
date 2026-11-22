namespace Quill.Editor.Inlines;

/// <summary>
/// The inline element types (02-document-model.md, inline element types), after Typora's md-inline kinds.
/// </summary>
public enum InlineKind
{
    /// <summary>Plain source text.</summary>
    Text,

    /// <summary>A backslash escape such as <c>\*</c>: the backslash is the marker, the character the content.</summary>
    Escape,

    /// <summary>An HTML entity such as <c>&amp;copy;</c>, shown as the character it stands for.</summary>
    Entity,

    Strong,
    Emphasis,
    Strikethrough,
    Highlight,
    Subscript,
    Superscript,

    /// <summary>A code span; the content is raw text.</summary>
    Code,

    /// <summary>Inline math; the content is the TeX source.</summary>
    Math,

    /// <summary>An inline or reference link. The children are the link text.</summary>
    Link,

    /// <summary>An inline or reference image. The children are the alt text.</summary>
    Image,

    /// <summary>An autolink in angle brackets: <c>&lt;https://example.com&gt;</c>.</summary>
    Autolink,

    /// <summary>A bare URL recognized by the GFM autolink extension: <c>www.example.com</c>.</summary>
    Url,

    /// <summary>A footnote reference <c>[^label]</c>; the content is the label.</summary>
    FootnoteReference,

    /// <summary>An emoji shortcode such as <c>:smile:</c>.</summary>
    Emoji,

    /// <summary>A soft line break ("\n").</summary>
    SoftBreak,

    /// <summary>A hard line break: trailing spaces or a backslash (the opening syntax) before "\n".</summary>
    HardBreak,

    /// <summary>A <c>&lt;br&gt;</c> tag (Q37).</summary>
    HtmlBreak,

    /// <summary>
    /// A paired whitelisted tag (<c>&lt;u&gt;</c>, <c>&lt;sub&gt;</c>, <c>&lt;sup&gt;</c>, <c>&lt;kbd&gt;</c>,
    /// Q37); the tags are its markers.
    /// </summary>
    HtmlSpan,

    /// <summary>Any other inline HTML, shown and kept as source (Q31).</summary>
    Html,
}

/// <summary>How a link or image names its destination.</summary>
public enum LinkForm
{
    /// <summary><c>[text](destination "title")</c>.</summary>
    Inline,

    /// <summary><c>[text][label]</c>.</summary>
    Full,

    /// <summary><c>[label][]</c>.</summary>
    Collapsed,

    /// <summary><c>[label]</c>.</summary>
    Shortcut,
}
