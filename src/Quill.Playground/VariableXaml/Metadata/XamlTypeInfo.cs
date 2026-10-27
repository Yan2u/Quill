using System.Linq.Expressions;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media.Animation;
using Quill.Playground.VariableXaml.Model;

namespace Quill.Playground.VariableXaml.Metadata;

/// <summary>How the content of an element (its child nodes) maps onto the content property.</summary>
public enum XamlContentKind
{
    /// <summary>The type declares no content property; child nodes are an error.</summary>
    None,

    /// <summary>Children go into a list (Children, Inlines, Blocks, Items...).</summary>
    Collection,

    /// <summary>A single child object (Border.Child, ContentControl.Content...).</summary>
    SingleObject,

    /// <summary>Text content sets a string property (Run.Text).</summary>
    Text,
}

/// <summary>
/// Cached reflection metadata for one XAML type: factory, content property and members.
/// </summary>
public sealed class XamlTypeInfo
{
    private static readonly BindingFlags StaticFlags = BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy;
    private static readonly BindingFlags InstanceFlags = BindingFlags.Public | BindingFlags.Instance;

    private readonly XamlSchema _schema;
    private readonly Dictionary<string, XamlMemberInfo?> _members = new(StringComparer.Ordinal);
    private readonly Dictionary<string, XamlMemberInfo?> _attachedMembers = new(StringComparer.Ordinal);
    private readonly Lazy<Func<object>?> _factory;
    private readonly Lazy<XamlMemberInfo?> _contentMember;

    internal XamlTypeInfo(XamlSchema schema, XamlName name, Type clrType)
    {
        _schema = schema;
        Name = name;
        ClrType = clrType;
        IsOpaque = IsOpaqueType(clrType);
        _factory = new Lazy<Func<object>?>(CreateFactory);
        _contentMember = new Lazy<XamlMemberInfo?>(FindContentMember);
    }

    public XamlName Name { get; }

    public Type ClrType { get; }

    /// <summary>
    /// Types that cannot be built or patched through their object model (templates, styles,
    /// resource dictionaries, animations). They are always created atomically with XamlReader and
    /// recreated when their markup changes.
    /// </summary>
    public bool IsOpaque { get; }

    public bool IsUIElement => typeof(UIElement).IsAssignableFrom(ClrType);

    public Func<object>? Factory => _factory.Value;

    public XamlMemberInfo? ContentMember => _contentMember.Value;

    public XamlContentKind ContentKind => ContentMember switch
    {
        null => XamlContentKind.None,
        { Category: XamlMemberCategory.Collection } => XamlContentKind.Collection,
        { ValueType: var type } when type == typeof(string) => XamlContentKind.Text,
        _ => XamlContentKind.SingleObject,
    };

    /// <summary>
    /// True when the content is a whitespace-significant collection (inline text), where XAML
    /// keeps single spaces between child elements.
    /// </summary>
    public bool IsWhitespaceSignificant =>
        ContentKind == XamlContentKind.Collection && typeof(Inline).IsAssignableFrom(ContentMember!.CollectionItemType);

    /// <summary>Text children of inline collections become Runs.</summary>
    public bool ContentAcceptsRuns => IsWhitespaceSignificant;

    /// <summary>
    /// A string member that replaces the content collection when set, e.g. TextBlock.Text
    /// (which rebuilds Inlines as a single Run). Null for most types.
    /// </summary>
    public string? ShadowingTextMember =>
        ContentMember?.Name == "Inlines" && GetMember("Text")?.ValueType == typeof(string) ? "Text" : null;

    /// <summary>Text children of object collections (ItemsControl.Items) stay strings.</summary>
    public bool ContentAcceptsStrings =>
        (ContentKind == XamlContentKind.Collection && ContentMember!.CollectionItemType == typeof(object))
        || (ContentKind == XamlContentKind.SingleObject && ContentMember!.ValueType == typeof(object));

    /// <summary>Gets a member of this type (own or inherited), or null when there is none.</summary>
    public XamlMemberInfo? GetMember(string name)
    {
        if (_members.TryGetValue(name, out XamlMemberInfo? member))
        {
            return member;
        }

        member = FindMember(name);
        _members[name] = member;
        return member;
    }

    /// <summary>Gets an attached member declared by this (owner) type, e.g. <c>Row</c> on Grid.</summary>
    public XamlMemberInfo? GetAttachedMember(string name)
    {
        if (_attachedMembers.TryGetValue(name, out XamlMemberInfo? member))
        {
            return member;
        }

        DependencyProperty? property = GetDependencyProperty(name);
        if (property is not null)
        {
            MethodInfo? getter = ClrType.GetMethod("Get" + name, StaticFlags);
            member = new XamlMemberInfo(name, getter?.ReturnType ?? typeof(object), property, null, null, isAttached: true);
        }

        _attachedMembers[name] = member;
        return member;
    }

    /// <summary>Resolves a member as written on an element of this type (plain or attached).</summary>
    public XamlMemberInfo? ResolveMember(XamlMemberName name) => name.Kind switch
    {
        XamlMemberKind.Plain => GetMember(name.Member),
        XamlMemberKind.Attached when name.Owner == Name => GetMember(name.Member) ?? GetAttachedMember(name.Member),
        XamlMemberKind.Attached => _schema.GetType(name.Owner)?.GetAttachedMember(name.Member),
        _ => null,
    };

    public override string ToString() => ClrType.FullName ?? ClrType.Name;

    private XamlMemberInfo? FindMember(string name)
    {
        PropertyInfo? property = FindProperty(name);
        DependencyProperty? dependencyProperty = GetDependencyProperty(name);
        if (property is not null || dependencyProperty is not null)
        {
            return new XamlMemberInfo(name, property?.PropertyType ?? typeof(object), dependencyProperty, property, null, isAttached: false);
        }

        EventInfo? @event = ClrType.GetEvent(name, InstanceFlags);
        return @event is null ? null : new XamlMemberInfo(name, @event.EventHandlerType!, null, null, @event, isAttached: false);
    }

    private PropertyInfo? FindProperty(string name)
    {
        // Walk from the most derived type so "new" re-declarations win over hidden base members.
        for (Type? type = ClrType; type is not null && type != typeof(object); type = type.BaseType)
        {
            PropertyInfo? property = type.GetProperty(name, InstanceFlags | BindingFlags.DeclaredOnly);
            if (property is not null && property.GetIndexParameters().Length == 0)
            {
                return property;
            }
        }

        return null;
    }

    private DependencyProperty? GetDependencyProperty(string name)
    {
        // CsWinRT projects dependency property identifiers as static properties (TextProperty { get; }).
        PropertyInfo? identifier = ClrType.GetProperty(name + "Property", StaticFlags);
        if (identifier?.PropertyType == typeof(DependencyProperty))
        {
            return identifier.GetValue(null) as DependencyProperty;
        }

        FieldInfo? field = ClrType.GetField(name + "Property", StaticFlags);
        return field?.GetValue(null) as DependencyProperty;
    }

    private XamlMemberInfo? FindContentMember()
    {
        for (Type? type = ClrType; type is not null; type = type.BaseType)
        {
            string? name = type.GetCustomAttribute<ContentPropertyAttribute>(inherit: false)?.Name;
            if (name is not null)
            {
                return GetMember(name);
            }
        }

        return null;
    }

    private Func<object>? CreateFactory()
    {
        if (ClrType.IsAbstract || ClrType.GetConstructor(Type.EmptyTypes) is null)
        {
            return null;
        }

        return Expression.Lambda<Func<object>>(Expression.Convert(Expression.New(ClrType), typeof(object))).Compile();
    }

    private static bool IsOpaqueType(Type type) =>
        typeof(FrameworkTemplate).IsAssignableFrom(type)
        || typeof(Style).IsAssignableFrom(type)
        || typeof(ResourceDictionary).IsAssignableFrom(type)
        || typeof(Timeline).IsAssignableFrom(type)
        || typeof(VisualStateGroup).IsAssignableFrom(type)
        || typeof(VisualState).IsAssignableFrom(type)
        || typeof(SetterBase).IsAssignableFrom(type);
}
