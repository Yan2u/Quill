using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;
using Quill.VariableXaml.Virtualization;

namespace Quill.VariableXaml.Reconciliation;

/// <summary>
/// The reconciler's record of one mounted node: the virtual node last applied and the live
/// object created for it (a "fiber" in React terms).
/// </summary>
internal sealed class LiveNode
{
    public LiveNode(VNode node, object? instance, XamlTypeInfo? type, LiveNode? parent)
    {
        Node = node;
        Instance = instance;
        Type = type;
        Parent = parent;
    }

    /// <summary>The virtual node whose state the live instance currently reflects.</summary>
    public VNode Node { get; set; }

    /// <summary>The live object: a DependencyObject, or a string for text items of object slots.</summary>
    public object? Instance { get; }

    public XamlTypeInfo? Type { get; }

    public LiveNode? Parent { get; set; }

    /// <summary>Content children, aligned index-by-index with <see cref="ChildCollection"/>.</summary>
    public List<LiveNode>? Children { get; set; }

    /// <summary>Cached adapter over the live content collection.</summary>
    public IChildCollection? ChildCollection { get; set; }

    /// <summary>State of property elements (<c>&lt;Grid.RowDefinitions&gt;</c>, <c>&lt;Border.Background&gt;</c>...).</summary>
    public Dictionary<XamlMemberName, LiveProperty>? Properties { get; set; }

    /// <summary>Event handlers attached from attributes, so they can be detached or swapped.</summary>
    public Dictionary<XamlMemberName, Delegate>? Events { get; set; }

    /// <summary>True for the error placeholder shown in place of an element that could not be created.</summary>
    public bool IsPlaceholder { get; init; }

    /// <summary>State owned by a virtual element handler (e.g. <c>vx:VirtualStack</c>).</summary>
    public IVirtualElementState? VirtualState { get; set; }
}

/// <summary>Live state of one property element.</summary>
internal sealed class LiveProperty
{
    public LiveProperty(VPropertyElement element, XamlMemberInfo member)
    {
        Element = element;
        Member = member;
    }

    public VPropertyElement Element { get; set; }

    public XamlMemberInfo Member { get; }

    /// <summary>For collection members: the adapter and the item nodes.</summary>
    public IChildCollection? Collection { get; set; }

    public List<LiveNode>? Items { get; set; }

    /// <summary>For single-valued members holding a patchable object (e.g. a brush).</summary>
    public LiveNode? Value { get; set; }
}
