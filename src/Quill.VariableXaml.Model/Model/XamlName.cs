namespace Quill.VariableXaml.Model;

/// <summary>
/// A XAML type name: an XML namespace plus a local name, e.g. <c>{presentation}TextBlock</c>.
/// </summary>
public readonly record struct XamlName(string Namespace, string LocalName)
{
    public static XamlName Presentation(string localName) => new(XamlNamespaces.Presentation, localName);

    public static XamlName VariableXaml(string localName) => new(XamlNamespaces.VariableXaml, localName);

    public bool IsEmpty => LocalName is null;

    public override string ToString() =>
        Namespace == XamlNamespaces.Presentation ? LocalName : $"{{{Namespace}}}{LocalName}";
}

/// <summary>
/// How a member name was written in markup.
/// </summary>
public enum XamlMemberKind
{
    /// <summary>A member of the element's own type, e.g. <c>Text</c>.</summary>
    Plain,

    /// <summary>An attached member qualified by its owner type, e.g. <c>Grid.Row</c>.</summary>
    Attached,

    /// <summary>A language directive such as <c>x:Name</c> or <c>vx:Key</c>.</summary>
    Directive,
}

/// <summary>
/// The name of an attribute or property element. Value equality makes it usable as a diff key.
/// </summary>
public readonly record struct XamlMemberName
{
    private XamlMemberName(XamlMemberKind kind, string? ownerNamespace, string? ownerLocalName, string member)
    {
        Kind = kind;
        OwnerNamespace = ownerNamespace;
        OwnerLocalName = ownerLocalName;
        Member = member;
    }

    public XamlMemberKind Kind { get; }

    /// <summary>
    /// For attached members, the owner type namespace; for directives, the directive namespace.
    /// </summary>
    public string? OwnerNamespace { get; }

    /// <summary>For attached members, the owner type local name (e.g. <c>Grid</c>).</summary>
    public string? OwnerLocalName { get; }

    public string Member { get; }

    public XamlName Owner => new(OwnerNamespace!, OwnerLocalName!);

    public static XamlMemberName Plain(string member) => new(XamlMemberKind.Plain, null, null, member);

    public static XamlMemberName Attached(XamlName owner, string member) =>
        new(XamlMemberKind.Attached, owner.Namespace, owner.LocalName, member);

    public static XamlMemberName Directive(string directiveNamespace, string member) =>
        new(XamlMemberKind.Directive, directiveNamespace, null, member);

    public bool IsDirective(string directiveNamespace, string member) =>
        Kind == XamlMemberKind.Directive && OwnerNamespace == directiveNamespace && Member == member;

    public override string ToString() => Kind switch
    {
        XamlMemberKind.Attached => $"{OwnerLocalName}.{Member}",
        XamlMemberKind.Directive => OwnerNamespace switch
        {
            XamlNamespaces.Xaml => $"x:{Member}",
            XamlNamespaces.VariableXaml => $"vx:{Member}",
            _ => $"{{{OwnerNamespace}}}{Member}",
        },
        _ => Member,
    };
}
