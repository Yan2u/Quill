namespace Quill.Editor.Documents;

/// <summary>
/// Which blocks hold text and where the caret may stand between blocks (02-document-model.md,
/// "虚拟段落").
/// </summary>
public static class BlockRules
{
    /// <summary>
    /// Non-text blocks: tables, code, math, HTML, front matter, rules, TOC and definitions; containers
    /// count as non-text on a side where their first (or last) child is non-text.
    /// </summary>
    public static bool IsNonText(Block block, bool atStart)
    {
        return block switch
        {
            Table or CodeFence or CodeBlock or MathBlock or HtmlBlock or FrontMatter or ThematicBreak or TocBlock
                or FootnoteDefinition or LinkReferenceDefinition => true,
            BlockQuote or ListBlock or ListItem when ((ContainerBlock)block).Children is { Count: > 0 } children =>
                IsNonText(atStart ? children[0] : children[^1], atStart),
            _ => false,
        };
    }

    /// <summary>Paragraphs and headings: blocks the caret can stand at the start or end of.</summary>
    public static bool IsTypable(Block block) => block is Paragraph or Heading;

    /// <summary>
    /// Blocks the caret stands on whole when Up or Down reaches them (a block caret, Q59, Q60): the TOC
    /// and thematic breaks.
    /// </summary>
    public static bool TakesBlockCaret(Block block) => block is TocBlock or ThematicBreak;

    /// <summary>
    /// Whether a virtual paragraph may hold the caret before child <paramref name="index"/> of
    /// <paramref name="container"/>: between two non-text blocks, or at the start or end of the
    /// document next to a non-text block. Only containers that can hold a paragraph qualify: not a
    /// list (its children are items), a table or a definition.
    /// </summary>
    public static bool AllowsVirtualParagraph(ContainerBlock container, int index)
    {
        var children = container.Children;
        if (index < 0 || index > children.Count || children.Count == 0 || container is not (Document or BlockQuote or ListItem))
        {
            return false;
        }

        bool previousNonText = index > 0 && IsNonText(children[index - 1], atStart: false);
        bool nextNonText = index < children.Count && IsNonText(children[index], atStart: true);
        if (index > 0 && index < children.Count)
        {
            return previousNonText && nextNonText;
        }

        return container is Document && (index == 0 ? nextNonText : previousNonText);
    }
}
