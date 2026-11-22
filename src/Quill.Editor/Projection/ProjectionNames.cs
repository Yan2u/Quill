using Quill.VariableXaml.Model;

namespace Quill.Editor.Projection;

/// <summary>
/// The element controls and attributes the projection declares (05-projection-and-view.md). The
/// controls are implemented in Quill.Editor.WinUI (M5); the names are the contract between the two.
/// VNodes carry semantics only: no style values.
/// </summary>
public static class ProjectionNames
{
    /// <summary>The XAML namespace of the element controls.</summary>
    public const string ElementNamespace = "using:Quill.Editor.WinUI.Elements";

    public static readonly XamlName Document = Element("MdDocument");
    public static readonly XamlName Paragraph = Element("MdParagraph");
    public static readonly XamlName Heading = Element("MdHeading");
    public static readonly XamlName CodeBlock = Element("MdCodeBlock");
    public static readonly XamlName CodeFence = Element("MdCodeFence");
    public static readonly XamlName CodeLanguage = Element("MdCodeLanguage");
    public static readonly XamlName SourceLine = Element("MdSourceLine");
    public static readonly XamlName MathBlock = Element("MdMathBlock");
    public static readonly XamlName HtmlBlock = Element("MdHtmlBlock");
    public static readonly XamlName FrontMatter = Element("MdFrontMatter");
    public static readonly XamlName ThematicBreak = Element("MdThematicBreak");
    public static readonly XamlName Toc = Element("MdToc");
    public static readonly XamlName BlockQuote = Element("MdBlockQuote");
    public static readonly XamlName List = Element("MdList");
    public static readonly XamlName ListItem = Element("MdListItem");
    public static readonly XamlName Table = Element("MdTable");
    public static readonly XamlName TableRow = Element("MdTableRow");
    public static readonly XamlName TableCell = Element("MdTableCell");
    public static readonly XamlName FootnoteDefinition = Element("MdFootnoteDefinition");
    public static readonly XamlName LinkReferenceDefinition = Element("MdLinkReferenceDefinition");
    public static readonly XamlName DefinitionSlot = Element("MdDefinitionSlot");
    public static readonly XamlName VirtualParagraph = Element("MdVirtualParagraph");
    public static readonly XamlName VirtualStack = XamlName.VariableXaml("VirtualStack");

    // Attributes. Values are typed (string, int, bool, enums, InlineContent, SelectionInfo).
    public const string BlockId = "BlockId";
    public const string Content = "Content";
    public const string Text = "Text";

    /// <summary>On the document node of a source document (11-source-mode.md): lines without block spacing.</summary>
    public const string IsSource = "IsSource";
    public const string IsFocused = "IsFocused";
    public const string IsActive = "IsActive";
    public const string IsSelected = "IsSelected";

    /// <summary>The caret stands on the block whole (Q59): drawn unlike a selection (Typora: a dashed frame around a TOC, a border around a rule).</summary>
    public const string HasBlockCaret = "HasBlockCaret";

    public const string SelectionStart = "SelectionStart";
    public const string SelectionEnd = "SelectionEnd";
    public const string CompositionStart = "CompositionStart";
    public const string CompositionEnd = "CompositionEnd";
    public const string Level = "Level";
    public const string Info = "Info";
    public const string IsOrdered = "IsOrdered";
    public const string Number = "Number";
    public const string Task = "Task";
    public const string Alignments = "Alignments";
    public const string Alignment = "Alignment";
    public const string IsHeader = "IsHeader";
    public const string SlotKind = "SlotKind";
    public const string BacklinkCount = "BacklinkCount";
    public const string Selection = "Selection";
    public const string Source = "Source";

    /// <summary>The entries of a TOC (<see cref="TocContent"/>, Q68).</summary>
    public const string Entries = "Entries";

    /// <summary>The hover flyout declared on the document node (<see cref="HoverInfo"/>).</summary>
    public const string Hover = "Hover";

    private static XamlName Element(string localName) => new(ElementNamespace, localName);
}
