using Quill.Editor.Documents;
using Quill.Editor.Documents.Syntax;

namespace Quill.Editor.Markdown.Export;

/// <summary>
/// Writes the document model as Markdown (06-markdown-io.md): recorded syntax is replayed so that
/// unchanged content is reproduced byte for byte; syntax is generated only for new or structurally
/// changed parts.
/// </summary>
internal sealed partial class BlockWriter
{
    private readonly MarkdownWriter _writer;
    private readonly EditorOptions _options;

    /// <summary>The content column of the last item of the list written last (relative to its container).</summary>
    private int _lastItemContentColumns = int.MaxValue;

    /// <summary>
    /// While a block right after a list is written: the indentation from which its first line would
    /// continue the list's last item. Recorded indentation that deep is dropped (it is syntax only),
    /// so that a block moved after a list stays out of it.
    /// </summary>
    private int _absorbedFrom = int.MaxValue;

    /// <summary>Footnote bodies being written: Markdig takes unindented lines into them, so columns do not compare there.</summary>
    private int _footnoteDepth;

    /// <summary>The block being written comes right after indented code, which indented code would join.</summary>
    private bool _codeAfterIndentedCode;

    /// <summary>The code block written last was written indented.</summary>
    private bool _wroteIndentedCode;

    private readonly LinkLabelPlaceholders _placeholders;

    private BlockWriter(EditorOptions options, Dictionary<BlockId, int>? blockStarts, IReadOnlyList<Block> roots)
    {
        _options = options;
        _writer = new MarkdownWriter(blockStarts);
        _placeholders = new LinkLabelPlaceholders(roots);
    }

    /// <param name="blockStarts">Receives the output offset where each block starts, when given.</param>
    public static string Write(Document document, EditorOptions options, Dictionary<BlockId, int>? blockStarts = null)
    {
        var writer = new BlockWriter(options, blockStarts, document.Children);
        writer.WriteBody(document.Children, BodyContext.Blocks, ownerItem: null, previousIsContent: false);

        // A last empty paragraph is a last empty line, which needs the line break after it to exist
        // (a single empty paragraph is the empty document instead).
        bool endsWithEmptyLine = document.Children.Count > 1 && document.Children[^1] is Paragraph { Text.Length: 0 };
        string markdown = writer._writer.Finish(document.Format.EndsWithLineBreak || endsWithEmptyLine);
        return document.Children is [ThematicBreak, ..] ? WithoutFrontMatterOpening(markdown) : markdown;
    }

    /// <summary>
    /// A rule written "---" as the document's first line opens YAML front matter when a line from the
    /// third on closes it ("---" or "...", as Markdig looks for it); written "***" it stays a rule. A
    /// rule read as the first line had no such line after it, so this only changes a document that
    /// edits made so.
    /// </summary>
    private static string WithoutFrontMatterOpening(string markdown)
    {
        int firstEnd = markdown.IndexOf('\n');
        if (firstEnd < 0 || markdown[..firstEnd].TrimEnd(' ', '\t') != "---")
        {
            return markdown;
        }

        foreach (var line in markdown[(firstEnd + 1)..].Split('\n').Skip(1))
        {
            if (line.TrimEnd() is "---" or "...")
            {
                return "***" + markdown[3..];
            }
        }

        return markdown;
    }

    /// <summary>Writes blocks as a stand-alone fragment, without a trailing line break.</summary>
    public static string WriteFragment(IReadOnlyList<Block> blocks, EditorOptions options)
    {
        var writer = new BlockWriter(options, blockStarts: null, blocks);
        writer.WriteBody(blocks, BodyContext.Blocks, ownerItem: null, previousIsContent: false);
        return writer._writer.Finish(endsWithLineBreak: false);
    }

    /// <summary>How separators are written in a body.</summary>
    /// <param name="InListItem">The body is a list item's children: separators follow the list's looseness.</param>
    /// <param name="Loose">Inside a list item, generated separators are a blank line (loose) or nothing (tight).</param>
    /// <param name="Regenerate">The enclosing list changed between tight and loose: ignore recorded separators.</param>
    private readonly record struct BodyContext(bool InListItem, bool Loose, bool Regenerate)
    {
        public static BodyContext Blocks => default;
    }

    /// <summary>
    /// Writes a container's children. Mirrors the importer: between two non-empty blocks there is one
    /// separator (recorded on the later block), followed by one blank line per empty paragraph.
    /// </summary>
    private void WriteBody(IReadOnlyList<Block> children, BodyContext context, ListItem? ownerItem, bool previousIsContent)
    {
        bool hasPrevious = previousIsContent;
        Block? previous = null;
        ListBlock? previousList = null;
        char previousListMarker = '\0';
        var pending = new List<Paragraph>();

        // Lines written since a list ended, while only separators and empty paragraphs followed it.
        int linesAfterList = -1;

        // The last block written other than empty paragraphs was indented code.
        bool afterIndentedCode = false;

        // In a list item, the empty paragraphs after its last other block (not its text).
        int trailingFrom = children.Count;
        while (ownerItem is not null && trailingFrom > 1 && children[trailingFrom - 1] is Paragraph { IsEmpty: true })
        {
            trailingFrom--;
        }

        void WriteEmpty(Paragraph empty, bool indented)
        {
            // The second line after a list, indented to its last item's content, would become an empty
            // paragraph of that item (Typora's form, Q56): the empty paragraph's whitespace is left out.
            if (linesAfterList == 1 && Columns(empty.Text) >= _lastItemContentColumns)
            {
                empty = empty with { Text = string.Empty };
            }

            if (indented)
            {
                _writer.MarkBlockStart(empty.Id);
                _writer.BeginLine(empty.PrefixAt(0), keepIndentation: true);
                _writer.WriteLines(empty.Text, empty.LinePrefixes);
            }
            else
            {
                WriteBlock(empty, context, ownerItem, isLast: false, listMarker: '\0');
            }

            if (linesAfterList >= 0)
            {
                linesAfterList++;
            }
        }

        for (int index = 0; index < children.Count; index++)
        {
            var child = children[index];
            // An empty paragraph is a blank line, except the text of a task item: it is written on the
            // marker line after the task marker, and what follows needs a separator after it.
            bool taskText = index == 0 && ownerItem is { Task: not TaskState.None };
            bool indentedEmpty = IsIndentedEmpty(child, index, ownerItem, trailing: index >= trailingFrom);
            if (child is Paragraph { IsEmpty: true } empty && !taskText && !indentedEmpty)
            {
                pending.Add(empty);
                continue;
            }

            if (hasPrevious)
            {
                // An ordered list not starting at 1 cannot interrupt a paragraph: it needs a blank line
                // after it, as Typora's auto-formatting writes. (An empty item can, as in Typora, Q56.)
                bool cannotInterrupt = previous is Paragraph && child is ListBlock { IsOrdered: true, Children: [ListItem { Number: not (null or 1) }, ..] };
                int separatorLines = WriteSeparator(child, previous, context, force: pending.Count > 0 || cannotInterrupt);
                if (linesAfterList >= 0)
                {
                    linesAfterList += separatorLines;
                }
            }

            foreach (var pendingEmpty in pending)
            {
                WriteEmpty(pendingEmpty, indented: false);
            }

            pending.Clear();

            char listMarker = '\0';
            if (child is ListBlock list)
            {
                listMarker = list.MarkerChar;
                if (_options.SwitchMarkerForAdjacentLists && previousList is not null
                    && previousList.IsOrdered == list.IsOrdered && previousListMarker == listMarker
                    && list.Syntax?.SeparateFrom != previousList.Id)
                {
                    listMarker = AlternateMarker(listMarker);
                }
            }

            // Blank lines and empty paragraphs after a list do not end it: a block indented far enough
            // would still join its last item.
            _absorbedFrom = linesAfterList >= 0 && _footnoteDepth == 0 ? _lastItemContentColumns : int.MaxValue;
            if (indentedEmpty)
            {
                WriteEmpty((Paragraph)child, indented: true);
            }
            else
            {
                _codeAfterIndentedCode = afterIndentedCode;
                WriteBlock(child, context, ownerItem, isLast: index == children.Count - 1, listMarker);
                _codeAfterIndentedCode = false;
                afterIndentedCode = child is CodeFence && _wroteIndentedCode;
                linesAfterList = child is ListBlock ? 0 : -1;
            }

            _absorbedFrom = int.MaxValue;
            hasPrevious = true;
            previous = child;
            if (!indentedEmpty)
            {
                // A list after an empty paragraph still follows the list before it: Markdown has no
                // empty paragraph to keep the two apart.
                previousList = child as ListBlock;
                previousListMarker = listMarker;
            }
        }

        foreach (var pendingEmpty in pending)
        {
            WriteEmpty(pendingEmpty, indented: false);
        }
    }

    /// <summary>
    /// An empty paragraph in a list item, after the item's text, written as a block of its own: a line
    /// holding the item's indentation, with separators around it, as Typora writes it (Q56). It is one
    /// when it was read that way (it has a separator), is new (it has no line recorded in the item), or
    /// comes after the item's last other block, where only this form is read back; one read as an
    /// extra blank line between blocks stays a blank line.
    /// </summary>
    private static bool IsIndentedEmpty(Block block, int index, ListItem? ownerItem, bool trailing) =>
        block is Paragraph { IsEmpty: true } paragraph && ownerItem is not null && index > 0
        && (trailing || paragraph.SeparatorBefore is not null || !paragraph.PrefixAt(0).TryGetSegment(ownerItem.Id, out _));

    /// <summary>
    /// Writes the separator before a block: the recorded one while the block still follows the same
    /// block as when it was recorded, otherwise a generated one. Empty paragraphs after the separator
    /// need at least one blank line.
    /// </summary>
    /// <returns>The number of lines written.</returns>
    private int WriteSeparator(Block block, Block? previous, BodyContext context, bool force)
    {
        var stored = block.SeparatorBefore;
        bool valid = stored is not null && !context.Regenerate && !(force && stored.Lines.IsEmpty)
            && (stored.Previous is null || stored.Previous == previous?.Id);
        if (valid)
        {
            foreach (var line in stored!.Lines)
            {
                _writer.BeginLine(line.Prefix);
                _writer.Write(line.Text);
            }

            return stored.Lines.Length;
        }

        int blankLines = context.InListItem && !context.Loose ? 0 : 1;
        if (force)
        {
            blankLines = 1;
        }

        for (int i = 0; i < blankLines; i++)
        {
            _writer.BeginLine(LinePrefix.None);
        }

        return blankLines;
    }

    private void WriteBlock(Block block, BodyContext context, ListItem? ownerItem, bool isLast, char listMarker)
    {
        _writer.MarkBlockStart(block.Id);
        switch (block)
        {
            case Paragraph paragraph:
                WriteParagraph(paragraph, ownerItem);
                break;
            case Heading heading:
                WriteHeading(heading);
                break;
            case CodeFence code:
                WriteCodeBlock(code, isLast);
                break;
            case MathBlock math:
                WriteFenced(math, math.Syntax, info: string.Empty, defaultChar: '$', defaultLength: 2, canLengthen: false, allowUnclosed: isLast);
                break;
            case FrontMatter frontMatter:
                WriteFenced(frontMatter, frontMatter.Syntax, info: string.Empty, defaultChar: '-', defaultLength: 3, canLengthen: false, allowUnclosed: false);
                break;
            case HtmlBlock html:
                WriteLeafLines(html);
                break;
            case ThematicBreak rule:
                WriteThematicBreak(rule);
                break;
            case TocBlock toc:
                WriteAtomic(toc, "[toc]");
                break;
            case BlockQuote quote:
                _writer.PushScope(new QuoteScope(quote.Id, _absorbedFrom));
                WriteBody(quote.Children, BodyContext.Blocks, ownerItem: null, previousIsContent: false);
                _writer.PopScope();
                break;
            case ListBlock list:
                WriteList(list, context, listMarker == '\0' ? list.MarkerChar : listMarker, ownerItem);
                break;
            case Table table:
                WriteTable(table);
                break;
            case FootnoteDefinition footnote:
                WriteFootnote(footnote);
                break;
            case LinkReferenceDefinition definition:
                WriteLinkDefinition(definition);
                break;
            default:
                throw new InvalidOperationException($"Block {block.Kind} cannot be written at this position.");
        }
    }

    /// <summary>
    /// Syntax text without its leading indentation where that indentation would change the structure:
    /// when it would join the block to the list before it, and on a list item's marker line, where
    /// it would join the marker's spacing and move the item's content column. Call before the line
    /// is begun.
    /// </summary>
    private string Unabsorbed(string text)
    {
        string trimmed = text.TrimStart(' ', '\t');
        bool drop = _writer.PendingItemBullet() is not null || Columns(text[..^trimmed.Length]) >= _absorbedFrom;
        return text.Length > trimmed.Length && drop ? trimmed : text;
    }

    private static char AlternateMarker(char marker) => marker switch
    {
        '.' => ')',
        ')' => '.',
        '-' => '*',
        _ => '-',
    };
}
