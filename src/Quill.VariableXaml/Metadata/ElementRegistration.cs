using Microsoft.UI.Xaml;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Metadata;

/// <summary>
/// An element type registered in code: a factory and typed member setters, so the reconciler
/// creates and patches it without XamlReader and without reflection (05-projection-and-view.md,
/// the <c>Md*</c> controls). Only the registered members exist; anything else is reported.
/// </summary>
public abstract class ElementRegistration
{
    private protected readonly Dictionary<string, XamlMemberInfo> MemberTable = new(StringComparer.Ordinal);

    private protected ElementRegistration(XamlName name, Type clrType)
    {
        Name = name;
        ClrType = clrType;
    }

    public XamlName Name { get; }

    public Type ClrType { get; }

    internal XamlMemberInfo? ContentMember { get; private protected set; }

    internal abstract object Create();

    internal XamlMemberInfo? GetMember(string name) => MemberTable.GetValueOrDefault(name);
}

/// <summary>The registration of element type <typeparamref name="T"/>; members are added fluently.</summary>
public sealed class ElementRegistration<T> : ElementRegistration
    where T : UIElement
{
    private readonly Func<T> _factory;

    public ElementRegistration(XamlName name, Func<T> factory)
        : base(name, typeof(T))
    {
        _factory = factory;
    }

    /// <summary>A property: <paramref name="set"/> applies a value, clearing applies <paramref name="defaultValue"/>.</summary>
    public ElementRegistration<T> Property<TValue>(string name, Action<T, TValue> set, TValue defaultValue = default!)
    {
        MemberTable[name] = XamlMemberInfo.Typed(
            name,
            typeof(TValue),
            XamlMemberCategory.Property,
            getter: null,
            setter: (target, value) => set((T)target, value is null ? defaultValue : (TValue)value),
            clear: target => set((T)target, defaultValue));
        return this;
    }

    /// <summary>The content is a list of child elements, e.g. the blocks of a container.</summary>
    public ElementRegistration<T> Children(string name, Func<T, IList<UIElement>> children)
    {
        var member = XamlMemberInfo.Typed(
            name,
            typeof(IList<UIElement>),
            XamlMemberCategory.Collection,
            getter: target => children((T)target),
            setter: null,
            clear: null);
        MemberTable[name] = member;
        ContentMember = member;
        return this;
    }

    /// <summary>The content is a single child element.</summary>
    public ElementRegistration<T> Child(string name, Func<T, UIElement?> get, Action<T, UIElement?> set)
    {
        var member = XamlMemberInfo.Typed(
            name,
            typeof(UIElement),
            XamlMemberCategory.Property,
            getter: target => get((T)target),
            setter: (target, value) => set((T)target, (UIElement?)value),
            clear: target => set((T)target, null));
        MemberTable[name] = member;
        ContentMember = member;
        return this;
    }

    internal override object Create() => _factory();
}
