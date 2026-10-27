namespace Quill.Playground.VariableXaml.Model;

public enum VNodeKind
{
    Element,
    Text,
}

/// <summary>
/// An immutable virtual XAML node: the "description" side of the reconciler.
/// </summary>
/// <remarks>
/// Every node carries a 64-bit structural <see cref="Hash"/> computed bottom-up at construction,
/// so the reconciler can skip unchanged subtrees in O(1). Unchanged subtrees can also be shared
/// by reference between successive trees (the incremental parser and memoized document builders
/// do this), which skips them without even comparing hashes.
/// </remarks>
public sealed class VNode
{
    private static readonly VAttribute[] NoAttributes = [];
    private static readonly VPropertyElement[] NoPropertyElements = [];
    private static readonly VNode[] NoChildren = [];

    private VNode(VNodeKind kind, XamlName type, string? key, string? text, VAttribute[] attributes, VPropertyElement[] propertyElements, VNode[] children)
    {
        Kind = kind;
        Type = type;
        Key = key;
        Text = text;
        AttributesArray = attributes;
        PropertyElementsArray = propertyElements;
        ChildrenArray = children;

        ulong hash = NodeHash.Combine(NodeHash.Seed, (ulong)kind);
        int count = 1;
        bool containsVirtual = kind == VNodeKind.Element && type.Namespace == XamlNamespaces.VariableXaml;
        if (kind == VNodeKind.Text)
        {
            hash = NodeHash.Combine(hash, text);
        }
        else
        {
            hash = NodeHash.Combine(NodeHash.Combine(hash, type), key);
            foreach (VAttribute attribute in attributes)
            {
                hash = NodeHash.Combine(NodeHash.Combine(NodeHash.Combine(hash, attribute.Name), attribute.Value), attribute.IsMarkup ? 1UL : 0UL);
            }

            foreach (VPropertyElement propertyElement in propertyElements)
            {
                hash = NodeHash.Combine(hash, propertyElement.Hash);
                foreach (VNode value in propertyElement.ValuesArray)
                {
                    count += value.Count;
                    containsVirtual |= value.ContainsVirtualElements;
                }
            }

            foreach (VNode child in children)
            {
                hash = NodeHash.Combine(hash, child.Hash);
                count += child.Count;
                containsVirtual |= child.ContainsVirtualElements;
            }
        }

        Hash = hash;
        Count = count;
        ContainsVirtualElements = containsVirtual;
    }

    public VNodeKind Kind { get; }

    /// <summary>The element type. Default for text nodes.</summary>
    public XamlName Type { get; }

    /// <summary>
    /// Reconciliation key (from <c>vx:Key</c>, then <c>x:Name</c>). Keyed siblings keep their
    /// live element across reorders; unkeyed siblings are matched by content hash, then by type.
    /// </summary>
    public string? Key { get; }

    /// <summary>The text of a text node; null for elements.</summary>
    public string? Text { get; }

    public IReadOnlyList<VAttribute> Attributes => AttributesArray;

    public IReadOnlyList<VPropertyElement> PropertyElements => PropertyElementsArray;

    public IReadOnlyList<VNode> Children => ChildrenArray;

    /// <summary>Structural hash of the whole subtree.</summary>
    public ulong Hash { get; }

    /// <summary>Number of nodes in the subtree, including this node.</summary>
    public int Count { get; }

    /// <summary>True when this node or a descendant is a VariableXaml virtual element (<c>vx:</c>).</summary>
    public bool ContainsVirtualElements { get; }

    public bool IsText => Kind == VNodeKind.Text;

    internal VAttribute[] AttributesArray { get; }

    internal VPropertyElement[] PropertyElementsArray { get; }

    internal VNode[] ChildrenArray { get; }

    public static VNode CreateText(string text) =>
        new(VNodeKind.Text, default, null, text, NoAttributes, NoPropertyElements, NoChildren);

    public static VNode CreateElement(
        XamlName type,
        IEnumerable<VAttribute>? attributes = null,
        IEnumerable<VNode>? children = null,
        IEnumerable<VPropertyElement>? propertyElements = null,
        string? key = null) =>
        CreateElementOwned(type, attributes?.ToArray(), children?.ToArray(), propertyElements?.ToArray(), key);

    /// <summary>
    /// Creates an element taking ownership of the given arrays (callers must not mutate them later).
    /// </summary>
    internal static VNode CreateElementOwned(XamlName type, VAttribute[]? attributes, VNode[]? children, VPropertyElement[]? propertyElements, string? key)
    {
        attributes ??= NoAttributes;
        key ??= FindKey(attributes);
        return new VNode(VNodeKind.Element, type, key, null, attributes, propertyElements ?? NoPropertyElements, children ?? NoChildren);
    }

    public object? GetAttribute(XamlMemberName name)
    {
        foreach (VAttribute attribute in AttributesArray)
        {
            if (attribute.Name == name)
            {
                return attribute.Value;
            }
        }

        return null;
    }

    /// <summary>Returns a copy with one child replaced; all other subtrees are shared.</summary>
    public VNode WithChild(int index, VNode child)
    {
        var children = (VNode[])ChildrenArray.Clone();
        children[index] = child;
        return new VNode(Kind, Type, Key, Text, AttributesArray, PropertyElementsArray, children);
    }

    /// <summary>Returns a copy with one property-element value replaced.</summary>
    public VNode WithPropertyValue(int propertyIndex, int valueIndex, VNode value)
    {
        var propertyElements = (VPropertyElement[])PropertyElementsArray.Clone();
        propertyElements[propertyIndex] = propertyElements[propertyIndex].WithValue(valueIndex, value);
        return new VNode(Kind, Type, Key, Text, AttributesArray, propertyElements, ChildrenArray);
    }

    public override string ToString() => IsText ? $"\"{Text}\"" : Key is null ? $"<{Type.LocalName}>" : $"<{Type.LocalName} key={Key}>";

    private static string? FindKey(VAttribute[] attributes)
    {
        string? name = null;
        foreach (VAttribute attribute in attributes)
        {
            if (attribute.Name.IsDirective(XamlNamespaces.VariableXaml, "Key"))
            {
                return attribute.Value?.ToString();
            }

            if (attribute.Name.IsDirective(XamlNamespaces.Xaml, "Name"))
            {
                name = attribute.Value?.ToString();
            }
        }

        return name;
    }
}
