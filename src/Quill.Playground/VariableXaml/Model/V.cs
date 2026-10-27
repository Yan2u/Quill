namespace Quill.Playground.VariableXaml.Model;

/// <summary>
/// Fluent C# construction of virtual trees, for code that renders a document model directly
/// (for example a Markdown AST) without going through XAML text.
/// </summary>
/// <example>
/// <code>
/// VNode node = V.Element("StackPanel").Set("Spacing", 8d)
///     .Add(V.Element("TextBlock").Key("title").Set("Text", "Hello").Set("FontSize", "28"))
///     .Add(V.Element("TextBlock").Set("Grid.Row", 1).Set("Text", "World"));
/// </code>
/// </example>
public static class V
{
    /// <summary>
    /// Starts an element. Names without a prefix are presentation types; <c>vx:</c> names are
    /// VariableXaml virtual elements.
    /// </summary>
    public static VElementBuilder Element(string typeName) => new(ParseTypeName(typeName));

    public static VElementBuilder Element(XamlName type) => new(type);

    public static VNode Text(string text) => VNode.CreateText(text);

    /// <summary>
    /// Parses a member name as written in markup: <c>Text</c>, <c>Grid.Row</c>, <c>x:Name</c>, <c>vx:Key</c>.
    /// </summary>
    public static XamlMemberName ParseMemberName(string name)
    {
        int colon = name.IndexOf(':');
        if (colon > 0)
        {
            string prefix = name[..colon];
            string member = name[(colon + 1)..];
            return prefix switch
            {
                "x" => XamlMemberName.Directive(XamlNamespaces.Xaml, member),
                "vx" => XamlMemberName.Directive(XamlNamespaces.VariableXaml, member),
                _ => throw new ArgumentException($"Unknown member prefix '{prefix}' in '{name}'.", nameof(name)),
            };
        }

        int dot = name.IndexOf('.');
        return dot > 0
            ? XamlMemberName.Attached(XamlName.Presentation(name[..dot]), name[(dot + 1)..])
            : XamlMemberName.Plain(name);
    }

    private static XamlName ParseTypeName(string typeName) =>
        typeName.StartsWith("vx:", StringComparison.Ordinal)
            ? XamlName.VariableXaml(typeName[3..])
            : XamlName.Presentation(typeName);
}

/// <summary>
/// Mutable builder for one virtual element; converts implicitly to <see cref="VNode"/>.
/// </summary>
public sealed class VElementBuilder
{
    private readonly XamlName _type;
    private readonly List<VAttribute> _attributes = [];
    private readonly List<VNode> _children = [];
    private List<VPropertyElement>? _propertyElements;
    private string? _key;

    internal VElementBuilder(XamlName type) => _type = type;

    public static implicit operator VNode(VElementBuilder builder) => builder.Build();

    public VElementBuilder Key(string? key)
    {
        _key = key;
        return this;
    }

    /// <summary>
    /// Sets a member. String values follow XAML conversion rules; other values are set as-is.
    /// Setting the same member twice keeps the last value.
    /// </summary>
    public VElementBuilder Set(string member, object? value) => Set(V.ParseMemberName(member), value);

    public VElementBuilder Set(XamlMemberName member, object? value)
    {
        int index = _attributes.FindIndex(attribute => attribute.Name == member);
        if (index >= 0)
        {
            _attributes[index] = new VAttribute(member, value);
        }
        else
        {
            _attributes.Add(new VAttribute(member, value));
        }

        return this;
    }

    public VElementBuilder Add(VNode? child)
    {
        if (child is not null)
        {
            _children.Add(child);
        }

        return this;
    }

    public VElementBuilder Add(string text) => Add(VNode.CreateText(text));

    public VElementBuilder AddRange(IEnumerable<VNode> children)
    {
        _children.AddRange(children);
        return this;
    }

    /// <summary>Adds a property element, e.g. <c>Property("Grid.RowDefinitions", rows...)</c>.</summary>
    public VElementBuilder Property(string member, params VNode[] values)
    {
        XamlMemberName name = V.ParseMemberName(member);
        if (name.Kind == XamlMemberKind.Attached && name.OwnerLocalName == _type.LocalName)
        {
            // "Grid.RowDefinitions" on a Grid is the Grid's own member, not an attached one.
            name = XamlMemberName.Plain(name.Member);
        }

        (_propertyElements ??= []).Add(VPropertyElement.Create(name, values));
        return this;
    }

    public VNode Build() => VNode.CreateElementOwned(_type, _attributes.ToArray(), _children.ToArray(), _propertyElements?.ToArray(), _key);
}
