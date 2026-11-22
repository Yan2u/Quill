using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Markup;

/// <summary>Where an element sits inside its parent node.</summary>
internal enum SlotKind
{
    /// <summary>The element is the document root.</summary>
    Root,

    /// <summary>The element is in the parent's <see cref="VNode.Children"/>.</summary>
    Child,

    /// <summary>The element is a value of one of the parent's property elements.</summary>
    PropertyValue,
}

/// <summary>
/// Source location of one element node. Spans are stored in document (pre-)order so a subtree
/// occupies a contiguous range of <see cref="SubtreeLength"/> entries.
/// </summary>
internal struct ElementSpan
{
    public VNode Node;

    /// <summary>Offset of the element's opening <c>&lt;</c>.</summary>
    public int Start;

    /// <summary>Offset just past the element's closing <c>&gt;</c>.</summary>
    public int End;

    /// <summary>Span index of the parent element, or -1 for the root.</summary>
    public int ParentIndex;

    /// <summary>Number of span entries in this subtree, including this one.</summary>
    public int SubtreeLength;

    public SlotKind Slot;

    /// <summary>Index into the parent's property elements (only for <see cref="SlotKind.PropertyValue"/>).</summary>
    public int PropertyIndex;

    /// <summary>Index into the parent's children or property-element values.</summary>
    public int SlotIndex;

    /// <summary>The namespace scope in effect where this element starts (before its own declarations).</summary>
    public NamespaceScope Scope;
}

/// <summary>
/// A parsed markup document: source text, virtual tree and element span table.
/// </summary>
public sealed class MarkupDocument
{
    internal MarkupDocument(string text, VNode root, ElementSpan[] spans)
    {
        Text = text;
        Root = root;
        Spans = spans;
    }

    public string Text { get; }

    public VNode Root { get; }

    /// <summary>Number of element nodes in the document.</summary>
    public int ElementCount => Spans.Length;

    internal ElementSpan[] Spans { get; }
}

/// <summary>Describes how a document was produced by <see cref="IncrementalMarkupParser"/>.</summary>
public enum ParseKind
{
    /// <summary>The text did not change; the previous document was reused.</summary>
    Unchanged,

    /// <summary>The whole text was parsed.</summary>
    Full,

    /// <summary>Only the smallest element enclosing the edit was parsed and spliced into the previous tree.</summary>
    Incremental,
}

/// <summary>The outcome of one (possibly incremental) parse.</summary>
public sealed record ParseOutcome(MarkupDocument Document, ParseKind Kind, int ParsedLength, string? ReparsedElement);
