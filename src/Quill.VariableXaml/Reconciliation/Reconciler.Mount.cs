using System.Diagnostics.CodeAnalysis;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Reconciliation;

/// <summary>Mounting: creating live objects for virtual nodes that have no live counterpart yet.</summary>
internal sealed partial class Reconciler
{
    /// <summary>Creates the live object(s) for a virtual subtree. The caller inserts the instance into its slot.</summary>
    internal LiveNode Mount(VNode node, LiveNode? parent, Type slotType)
    {
        if (node.IsText)
        {
            return MountText(node, parent, slotType);
        }

        if (_virtualHandlers.TryGetValue(node.Type, out var handler))
        {
            return handler.Mount(this, node, parent);
        }

        XamlTypeInfo? type = _schema.GetType(node.Type);
        if (type is null)
        {
            return CreatePlaceholder(node, parent, slotType, $"Unknown element <{node.Type.LocalName}>");
        }

        if (!slotType.IsAssignableFrom(type.ClrType))
        {
            return CreatePlaceholder(node, parent, slotType, $"<{node.Type.LocalName}> is not a {slotType.Name}");
        }

        bool useXamlReader = !type.IsRegistered && (MountWithXamlReader || type.IsOpaque) && !node.ContainsVirtualElements;
        if (useXamlReader && TryMountWithXamlReader(node, type, parent, out LiveNode? mounted))
        {
            return mounted;
        }

        return type.IsOpaque
            ? CreatePlaceholder(node, parent, slotType, $"<{node.Type.LocalName}> could not be created")
            : MountWithObjectModel(node, type, parent, slotType);
    }

    private LiveNode MountText(VNode node, LiveNode? parent, Type slotType)
    {
        Statistics.NodesCreated++;
        object instance = slotType == typeof(object) ? node.Text!
            : slotType.IsAssignableFrom(typeof(Run)) ? new Run { Text = node.Text }
            : slotType.IsAssignableFrom(typeof(TextBlock)) ? new TextBlock { Text = node.Text, TextWrapping = TextWrapping.Wrap }
            : node.Text!;
        return new LiveNode(node, instance, null, parent);
    }

    private bool TryMountWithXamlReader(VNode node, XamlTypeInfo type, LiveNode? parent, [NotNullWhen(true)] out LiveNode? live)
    {
        live = null;
        string markup;
        try
        {
            markup = _writer.Write(node);
        }
        catch (NotSupportedException exception)
        {
            if (type.IsOpaque)
            {
                Report($"<{node.Type.LocalName}> cannot be created: {exception.Message}");
            }

            return false;
        }

        object instance;
        try
        {
            Statistics.XamlReaderLoads++;
            instance = XamlReader.Load(markup);
        }
        catch (Exception exception)
        {
            // The object model path re-applies attributes one by one and reports the precise failure.
            if (type.IsOpaque)
            {
                Report($"XamlReader could not create <{node.Type.LocalName}>: {FirstLine(exception.Message)}");
            }

            return false;
        }

        try
        {
            live = Bind(node, instance, type, parent);
            Statistics.NodesCreated += node.Count;
            return true;
        }
        catch (BindingMismatchException exception)
        {
            Report($"XamlReader output for <{node.Type.LocalName}> did not match its markup ({exception.Message}); used the object model.");
            return false;
        }
    }

    /// <summary>
    /// Walks an object tree created by XamlReader alongside its virtual tree and records the
    /// pairing, then applies what the markup writer could not express (typed values, events).
    /// </summary>
    private LiveNode Bind(VNode node, object instance, XamlTypeInfo type, LiveNode? parent)
    {
        var live = new LiveNode(node, instance, type, parent);
        foreach (VAttribute attribute in node.AttributesArray)
        {
            bool isEvent = attribute.Name.Kind != XamlMemberKind.Directive && type.ResolveMember(attribute.Name)?.Category == XamlMemberCategory.Event;
            if (attribute.Value is not string || isEvent)
            {
                ApplyAttribute(live, attribute);
            }
        }

        if (type.IsOpaque)
        {
            return live;
        }

        foreach (VPropertyElement element in node.PropertyElementsArray)
        {
            BindPropertyElement(live, element);
        }

        VNode[] children = node.ChildrenArray;
        switch (type.ContentKind)
        {
            // Without children the collection is not ours to track (TextBlock.Text fills Inlines itself).
            case XamlContentKind.Collection when children.Length > 0:
                IChildCollection collection = ChildCollection.Wrap(type.ContentMember!.GetValue(instance))
                    ?? throw new BindingMismatchException("content is not a list");
                live.ChildCollection = collection;
                if (collection.Count != children.Length)
                {
                    throw new BindingMismatchException($"{collection.Count} items for {children.Length} children");
                }

                live.Children = BindItems(children, collection, live);
                break;
            case XamlContentKind.SingleObject when children.Length == 1:
                live.Children = [BindItem(children[0], type.ContentMember!.GetValue(instance), live)];
                break;
            case XamlContentKind.SingleObject when children.Length > 1:
            case XamlContentKind.None when children.Length > 0:
                throw new BindingMismatchException("unexpected children");
        }

        return live;
    }

    private List<LiveNode> BindItems(VNode[] nodes, IChildCollection collection, LiveNode parent)
    {
        var items = new List<LiveNode>(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            items.Add(BindItem(nodes[i], collection.GetAt(i), parent));
        }

        return items;
    }

    private LiveNode BindItem(VNode node, object? item, LiveNode parent)
    {
        if (node.IsText)
        {
            return item is Run or string or TextBlock
                ? new LiveNode(node, item, null, parent)
                : throw new BindingMismatchException($"text bound to {item?.GetType().Name ?? "null"}");
        }

        XamlTypeInfo type = _schema.GetType(node.Type)!;
        if (item is null || !type.ClrType.IsInstanceOfType(item))
        {
            throw new BindingMismatchException($"<{node.Type.LocalName}> bound to {item?.GetType().Name ?? "null"}");
        }

        return Bind(node, item, type, parent);
    }

    private void BindPropertyElement(LiveNode live, VPropertyElement element)
    {
        XamlMemberInfo member = live.Type!.ResolveMember(element.Member)
            ?? throw new BindingMismatchException($"unknown property element {element.Member}");
        var property = new LiveProperty(element, member);
        (live.Properties ??= [])[element.Member] = property;
        switch (member.Category)
        {
            case XamlMemberCategory.Collection:
                IChildCollection collection = ChildCollection.Wrap(member.GetValue(live.Instance!))
                    ?? throw new BindingMismatchException($"{element.Member} is not a list");
                if (collection.Count != element.ValuesArray.Length)
                {
                    throw new BindingMismatchException($"{collection.Count} items for {element.ValuesArray.Length} values of {element.Member}");
                }

                property.Collection = collection;
                property.Items = BindItems(element.ValuesArray, collection, live);
                break;
            case XamlMemberCategory.Property when element.ValuesArray is [{ IsText: false } value]:
                property.Value = BindItem(value, member.GetValue(live.Instance!), live);
                break;

            // Dictionaries and text values are tracked by hash only and recreated when they change.
        }
    }

    private LiveNode MountWithObjectModel(VNode node, XamlTypeInfo type, LiveNode? parent, Type slotType)
    {
        if (type.Factory is null)
        {
            return CreatePlaceholder(node, parent, slotType, $"<{node.Type.LocalName}> has no public parameterless constructor");
        }

        object instance;
        try
        {
            instance = type.Factory();
        }
        catch (Exception exception)
        {
            return CreatePlaceholder(node, parent, slotType, $"Creating <{node.Type.LocalName}> failed: {FirstLine(exception.Message)}");
        }

        Statistics.NodesCreated++;
        var live = new LiveNode(node, instance, type, parent);
        foreach (VAttribute attribute in node.AttributesArray)
        {
            ApplyAttribute(live, attribute);
        }

        foreach (VPropertyElement element in node.PropertyElementsArray)
        {
            MountPropertyElement(live, element);
        }

        MountContent(live, node);
        return live;
    }

    private void MountContent(LiveNode live, VNode node)
    {
        XamlTypeInfo type = live.Type!;
        object instance = live.Instance!;
        switch (type.ContentKind)
        {
            case XamlContentKind.Collection:
            {
                IChildCollection collection = ChildCollection.Wrap(type.ContentMember!.GetValue(instance))!;
                live.ChildCollection = collection;
                Type slotType = type.ContentMember.CollectionItemType!;
                VNode[] children = PrepareChildren(node.ChildrenArray, slotType, node);
                if (children.Length > 0)
                {
                    live.Children = new List<LiveNode>(children.Length);
                    foreach (VNode child in children)
                    {
                        LiveNode mounted = Mount(child, live, slotType);
                        collection.Insert(collection.Count, mounted.Instance);
                        live.Children.Add(mounted);
                    }
                }

                break;
            }

            case XamlContentKind.SingleObject:
            {
                Type slotType = type.ContentMember!.ValueType;
                VNode[] children = PrepareChildren(node.ChildrenArray, slotType, node);
                if (children.Length > 1)
                {
                    Report($"<{node.Type.LocalName}> accepts a single child; extra children are ignored.");
                }

                if (children.Length > 0)
                {
                    LiveNode mounted = Mount(children[0], live, slotType);
                    if (mounted.Instance is not null)
                    {
                        type.ContentMember.SetValue(instance, mounted.Instance);
                    }

                    live.Children = [mounted];
                }

                break;
            }

            case XamlContentKind.Text when node.ChildrenArray.Length > 0:
                type.ContentMember!.SetValue(instance, JoinText(node.ChildrenArray));
                break;
            case XamlContentKind.None when node.ChildrenArray.Length > 0:
                Report($"<{node.Type.LocalName}> has no content property; its children are ignored.");
                break;
        }
    }

    private void MountPropertyElement(LiveNode live, VPropertyElement element)
    {
        XamlMemberInfo? member = live.Type!.ResolveMember(element.Member);
        if (member is null)
        {
            Report($"Unknown property element {element.Member} on <{live.Node.Type.LocalName}>.");
            return;
        }

        var property = new LiveProperty(element, member);
        (live.Properties ??= [])[element.Member] = property;
        SetPropertyElementValue(live, property, element);
    }

    /// <summary>Creates the value(s) of a property element and assigns them to the member.</summary>
    private void SetPropertyElementValue(LiveNode live, LiveProperty property, VPropertyElement element)
    {
        XamlMemberInfo member = property.Member;
        object instance = live.Instance!;
        property.Element = element;
        try
        {
            switch (member.Category)
            {
                case XamlMemberCategory.Collection:
                {
                    IChildCollection collection = property.Collection ??= ChildCollection.Wrap(member.GetValue(instance))!;
                    Type slotType = member.CollectionItemType!;
                    VNode[] values = PrepareChildren(element.ValuesArray, slotType, live.Node);
                    property.Items = new List<LiveNode>(values.Length);
                    foreach (VNode value in values)
                    {
                        LiveNode mounted = Mount(value, live, slotType);
                        collection.Insert(collection.Count, mounted.Instance);
                        property.Items.Add(mounted);
                    }

                    break;
                }

                case XamlMemberCategory.Dictionary:
                {
                    VNode dictionary = VNode.CreateElement(XamlName.Presentation(nameof(ResourceDictionary)), children: element.ValuesArray);
                    LiveNode mounted = Mount(dictionary, live, typeof(ResourceDictionary));
                    if (mounted.Instance is not null)
                    {
                        member.SetValue(instance, mounted.Instance);
                        Statistics.PropertiesSet++;
                    }

                    break;
                }

                default:
                {
                    if (element.ValuesArray.Length != 1)
                    {
                        Report($"{element.Member} expects exactly one value.");
                        break;
                    }

                    VNode value = element.ValuesArray[0];
                    if (value.IsText)
                    {
                        member.SetValue(instance, _converter.FromString(member.ValueType, value.Text!));
                    }
                    else
                    {
                        LiveNode mounted = Mount(value, live, member.ValueType);
                        if (mounted.Instance is not null)
                        {
                            member.SetValue(instance, mounted.Instance);
                        }

                        property.Value = mounted;
                    }

                    Statistics.PropertiesSet++;
                    break;
                }
            }
        }
        catch (Exception exception)
        {
            Report($"Setting {element.Member} on <{live.Node.Type.LocalName}> failed: {FirstLine(exception.Message)}");
        }
    }

    private static string JoinText(VNode[] children) =>
        string.Concat(children.Where(child => child.IsText).Select(child => child.Text));

    private static string FirstLine(string text)
    {
        int index = text.IndexOfAny(['\r', '\n']);
        return index < 0 ? text : text[..index];
    }

    private sealed class BindingMismatchException(string message) : Exception(message);
}
