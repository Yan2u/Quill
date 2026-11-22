using Quill.Editor.Documents;
using Quill.Editor.Projection;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// Registers the element controls with VariableXaml (05: <c>Md*</c> controls are created by
/// registered factories and patched through typed setters, never through XamlReader or reflection).
/// Every element and attribute the projection declares (<see cref="ProjectionNames"/>) is covered.
/// </summary>
public static class MarkdownElements
{
    private static readonly object Gate = new();
    private static readonly HashSet<XamlSchema> Registered = [];

    /// <summary>Registers every element once per schema; <see cref="XamlSchema.Default"/> when none is given.</summary>
    public static void Register(XamlSchema? schema = null)
    {
        schema ??= XamlSchema.Default;
        lock (Gate)
        {
            if (!Registered.Add(schema))
            {
                return;
            }
        }

        schema.Register(new ElementRegistration<MdDocument>(ProjectionNames.Document, static () => new())
            .Property<SelectionInfo?>(ProjectionNames.Selection, static (e, v) => e.Selection = v, null)
            .Property<HoverInfo?>(ProjectionNames.Hover, static (e, v) => e.Hover = v, null)
            .Property<bool>(ProjectionNames.IsSource, static (e, v) => e.IsSource = v)
            .Child("Body", static e => e.Body, static (e, v) => e.Body = v));

        schema.Register(Text(new ElementRegistration<MdParagraph>(ProjectionNames.Paragraph, static () => new())));
        schema.Register(Text(new ElementRegistration<MdHeading>(ProjectionNames.Heading, static () => new()))
            .Property<int>(ProjectionNames.Level, static (e, v) => e.Level = v, 1));
        schema.Register(Text(new ElementRegistration<MdTableCell>(ProjectionNames.TableCell, static () => new()))
            .Property<TableAlignment>(ProjectionNames.Alignment, static (e, v) => e.Alignment = v)
            .Property<bool>(ProjectionNames.IsHeader, static (e, v) => e.IsHeader = v));
        schema.Register(Text(new ElementRegistration<MdCodeLanguage>(ProjectionNames.CodeLanguage, static () => new())));
        schema.Register(Container(new ElementRegistration<MdCodeFence>(ProjectionNames.CodeFence, static () => new())));
        schema.Register(Text(new ElementRegistration<MdDefinitionSlot>(ProjectionNames.DefinitionSlot, static () => new()))
            .Property<DefinitionSlotKind>(ProjectionNames.SlotKind, static (e, v) => e.SlotKind = v));

        schema.Register(Raw(new ElementRegistration<MdCodeBlock>(ProjectionNames.CodeBlock, static () => new()))
            .Property<string>(ProjectionNames.Info, static (e, v) => e.Info = v, string.Empty));
        schema.Register(Raw(new ElementRegistration<MdMathBlock>(ProjectionNames.MathBlock, static () => new())));
        schema.Register(Raw(new ElementRegistration<MdFrontMatter>(ProjectionNames.FrontMatter, static () => new())));
        schema.Register(Raw(new ElementRegistration<MdHtmlBlock>(ProjectionNames.HtmlBlock, static () => new())));
        schema.Register(Raw(new ElementRegistration<MdSourceLine>(ProjectionNames.SourceLine, static () => new())));

        schema.Register(Atom(new ElementRegistration<MdThematicBreak>(ProjectionNames.ThematicBreak, static () => new())));
        schema.Register(Atom(new ElementRegistration<MdToc>(ProjectionNames.Toc, static () => new()))
            .Property<string>(ProjectionNames.Source, static (e, v) => e.Source = v, "[toc]")
            .Property<bool>(ProjectionNames.IsActive, static (e, v) => e.IsActive = v)
            .Property<TocContent>(ProjectionNames.Entries, static (e, v) => e.Entries = v, TocContent.Empty));

        schema.Register(Container(new ElementRegistration<MdBlockQuote>(ProjectionNames.BlockQuote, static () => new())));
        schema.Register(Container(new ElementRegistration<MdList>(ProjectionNames.List, static () => new()))
            .Property<int>(ProjectionNames.Level, static (e, v) => e.Level = v, 1)
            .Property<bool>(ProjectionNames.IsOrdered, static (e, v) => e.IsOrdered = v));
        schema.Register(Container(new ElementRegistration<MdListItem>(ProjectionNames.ListItem, static () => new()))
            .Property<int>(ProjectionNames.Level, static (e, v) => e.Level = v, 1)
            .Property<int>(ProjectionNames.Number, static (e, v) => e.Number = v)
            .Property<TaskState>(ProjectionNames.Task, static (e, v) => e.Task = v));
        schema.Register(Container(new ElementRegistration<MdTable>(ProjectionNames.Table, static () => new()))
            .Property<string>(ProjectionNames.Alignments, static (e, v) => e.Alignments = v, string.Empty));
        schema.Register(Container(new ElementRegistration<MdTableRow>(ProjectionNames.TableRow, static () => new()))
            .Property<bool>(ProjectionNames.IsHeader, static (e, v) => e.IsHeader = v));
        schema.Register(Container(new ElementRegistration<MdFootnoteDefinition>(ProjectionNames.FootnoteDefinition, static () => new()))
            .Property<int>(ProjectionNames.BacklinkCount, static (e, v) => e.BacklinkCount = v));
        schema.Register(Container(new ElementRegistration<MdLinkReferenceDefinition>(ProjectionNames.LinkReferenceDefinition, static () => new())));

        schema.Register(Block(new ElementRegistration<MdVirtualParagraph>(ProjectionNames.VirtualParagraph, static () => new()))
            .Property<int>(ProjectionNames.Number, static (e, v) => e.Number = v));
    }

    /// <summary>
    /// Prepares a VariableXamlRoot to show projected documents: registers the elements and gives
    /// its virtual stacks the block layout that keeps measured heights (Q69).
    /// </summary>
    public static void Configure(Quill.VariableXaml.VariableXamlRoot root)
    {
        Register();
        root.VirtualLayoutFactory = static () => new Layout.BlockStackLayout();
    }

    private static ElementRegistration<T> Block<T>(ElementRegistration<T> registration)
        where T : MdBlockView =>
        registration
            .Property<string>(ProjectionNames.BlockId, static (e, v) => e.BlockId = v, string.Empty)
            .Property<bool>(ProjectionNames.IsSelected, static (e, v) => e.IsSelected = v);

    private static ElementRegistration<T> Leaf<T>(ElementRegistration<T> registration)
        where T : MdTextBlockView =>
        Block(registration)
            .Property<bool>(ProjectionNames.IsFocused, static (e, v) => e.IsFocused = v)
            .Property<int>(ProjectionNames.SelectionStart, static (e, v) => e.SelectionStart = v, -1)
            .Property<int>(ProjectionNames.SelectionEnd, static (e, v) => e.SelectionEnd = v, -1)
            .Property<int>(ProjectionNames.CompositionStart, static (e, v) => e.CompositionStart = v, -1)
            .Property<int>(ProjectionNames.CompositionEnd, static (e, v) => e.CompositionEnd = v, -1);

    private static ElementRegistration<T> Text<T>(ElementRegistration<T> registration)
        where T : MdInlineTextView =>
        Leaf(registration).Property<InlineContent>(ProjectionNames.Content, static (e, v) => e.Content = v, InlineContent.Empty);

    private static ElementRegistration<T> Raw<T>(ElementRegistration<T> registration)
        where T : MdRawTextView =>
        Leaf(registration).Property<string>(ProjectionNames.Text, static (e, v) => e.Text = v, string.Empty);

    private static ElementRegistration<T> Atom<T>(ElementRegistration<T> registration)
        where T : MdAtomView =>
        Block(registration).Property<bool>(ProjectionNames.HasBlockCaret, static (e, v) => e.HasBlockCaret = v);

    private static ElementRegistration<T> Container<T>(ElementRegistration<T> registration)
        where T : MdContainerView =>
        Block(registration)
            .Property<bool>(ProjectionNames.IsActive, static (e, v) => e.IsActive = v)
            .Children("Blocks", static e => e.Blocks);
}
