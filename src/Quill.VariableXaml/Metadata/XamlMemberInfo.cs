using System.Reflection;
using Microsoft.UI.Xaml;

namespace Quill.VariableXaml.Metadata;

public enum XamlMemberCategory
{
    /// <summary>An ordinary settable property (dependency property and/or CLR property).</summary>
    Property,

    /// <summary>A read-only property whose value is a mutable list (Children, Inlines, RowDefinitions...).</summary>
    Collection,

    /// <summary>A read/write property whose value is a dictionary (Resources).</summary>
    Dictionary,

    /// <summary>A CLR event.</summary>
    Event,
}

/// <summary>
/// Runtime accessor for one XAML member: a dependency property, a CLR property or an event.
/// Lookups are reflection-based and cached by <see cref="XamlTypeInfo"/>, except for the typed
/// accessors of registered elements (<see cref="ElementRegistration"/>).
/// </summary>
public sealed class XamlMemberInfo
{
    private readonly Func<object, object?>? _getter;
    private readonly Action<object, object?>? _setter;
    private readonly Action<object>? _clear;

    internal XamlMemberInfo(string name, Type valueType, DependencyProperty? dependencyProperty, PropertyInfo? property, EventInfo? @event, bool isAttached)
    {
        Name = name;
        ValueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        DependencyProperty = dependencyProperty;
        Property = property;
        Event = @event;
        IsAttached = isAttached;
        Category = ClassifyCategory();
        CollectionItemType = Category == XamlMemberCategory.Collection ? FindListItemType(ValueType) : null;
    }

    private XamlMemberInfo(string name, Type valueType, XamlMemberCategory category, Func<object, object?>? getter, Action<object, object?>? setter, Action<object>? clear)
    {
        Name = name;
        ValueType = Nullable.GetUnderlyingType(valueType) ?? valueType;
        Category = category;
        CollectionItemType = category == XamlMemberCategory.Collection ? FindListItemType(ValueType) : null;
        _getter = getter;
        _setter = setter;
        _clear = clear;
    }

    public string Name { get; }

    /// <summary>The declared value type (nullable wrappers removed).</summary>
    public Type ValueType { get; }

    public DependencyProperty? DependencyProperty { get; }

    public PropertyInfo? Property { get; }

    public EventInfo? Event { get; }

    public bool IsAttached { get; }

    public XamlMemberCategory Category { get; }

    /// <summary>For collection members, the element type of the list (e.g. UIElement, Inline).</summary>
    public Type? CollectionItemType { get; }

    public bool IsWritable => _setter is not null || DependencyProperty is not null || Property?.CanWrite == true;

    /// <summary>Whether the member is a typed accessor of a registered element.</summary>
    public bool IsTyped => _getter is not null || _setter is not null;

    public object? GetValue(object target)
    {
        if (_getter is not null)
        {
            return _getter(target);
        }

        if (DependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            return dependencyObject.GetValue(DependencyProperty);
        }

        return Property?.GetValue(target);
    }

    public void SetValue(object target, object? value)
    {
        if (_setter is not null)
        {
            _setter(target, value);
            return;
        }

        if (DependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            dependencyObject.SetValue(DependencyProperty, value);
            return;
        }

        if (Property?.CanWrite != true)
        {
            throw new InvalidOperationException($"Member '{Name}' is not writable.");
        }

        Property.SetValue(target, value);
    }

    /// <summary>
    /// Restores the default value: ClearValue for dependency properties, otherwise the CLR default.
    /// </summary>
    public void ClearValue(object target)
    {
        if (_clear is not null)
        {
            _clear(target);
            return;
        }

        if (DependencyProperty is not null && target is DependencyObject dependencyObject)
        {
            dependencyObject.ClearValue(DependencyProperty);
            return;
        }

        if (Property?.CanWrite == true)
        {
            object? empty = ValueType == typeof(string) ? string.Empty : ValueType.IsValueType ? Activator.CreateInstance(ValueType) : null;
            Property.SetValue(target, empty);
        }
    }

    public override string ToString() => Name;

    internal static XamlMemberInfo Typed(string name, Type valueType, XamlMemberCategory category, Func<object, object?>? getter, Action<object, object?>? setter, Action<object>? clear) =>
        new(name, valueType, category, getter, setter, clear);

    private XamlMemberCategory ClassifyCategory()
    {
        if (Event is not null)
        {
            return XamlMemberCategory.Event;
        }

        if (typeof(ResourceDictionary).IsAssignableFrom(ValueType))
        {
            return XamlMemberCategory.Dictionary;
        }

        bool readOnly = Property?.CanWrite != true;
        bool isList = ValueType != typeof(string) && FindListItemType(ValueType) is not null;
        return isList && (readOnly || IsKnownCollectionType(ValueType)) ? XamlMemberCategory.Collection : XamlMemberCategory.Property;
    }

    private static bool IsKnownCollectionType(Type type) => type.Name.EndsWith("Collection", StringComparison.Ordinal);

    internal static Type? FindListItemType(Type type)
    {
        foreach (Type candidate in type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces())
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }

        return null;
    }
}
