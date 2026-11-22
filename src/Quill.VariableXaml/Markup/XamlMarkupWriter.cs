using System.Text;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Markup;

/// <summary>
/// Serializes virtual subtrees back to XAML so new content can be created with a single
/// <c>XamlReader.Load</c> call (the fastest dynamic creation path measured in the probes).
/// </summary>
/// <remarks>
/// The output is normalized so that every virtual child maps to exactly one item of the
/// created content collection: text inside inline collections is written as explicit
/// <c>&lt;Run Text="..."/&gt;</c>, text for single-valued content becomes the content attribute,
/// and text items of object collections become <c>&lt;x:String&gt;</c>. Typed (non-string) values,
/// event attributes and VariableXaml directives are omitted; the reconciler applies them after load.
/// </remarks>
internal sealed class XamlMarkupWriter
{
    private readonly XamlSchema _schema;

    public XamlMarkupWriter(XamlSchema schema) => _schema = schema;

    /// <exception cref="NotSupportedException">The subtree cannot be expressed for XamlReader.</exception>
    public string Write(VNode root)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [XamlNamespaces.Presentation] = string.Empty,
            [XamlNamespaces.Xaml] = "x",
        };
        CollectNamespaces(root, prefixes);

        var builder = new StringBuilder(64 + root.Count * 48);
        WriteElement(builder, root, prefixes, writeDeclarations: true);
        return builder.ToString();
    }

    /// <summary>Writes a one-attribute element used to let XamlReader evaluate a single value.</summary>
    public string WriteProbe(XamlName elementType, VAttribute attribute)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [XamlNamespaces.Presentation] = string.Empty,
            [XamlNamespaces.Xaml] = "x",
        };
        AddNamespace(elementType.Namespace, prefixes);
        if (attribute.Name.Kind == XamlMemberKind.Attached)
        {
            AddNamespace(attribute.Name.OwnerNamespace!, prefixes);
        }

        var builder = new StringBuilder();
        builder.Append('<').Append(Qualify(elementType, prefixes));
        WriteDeclarations(builder, prefixes);
        WriteAttribute(builder, attribute, prefixes);
        builder.Append("/>");
        return builder.ToString();
    }

    private void WriteElement(StringBuilder builder, VNode node, Dictionary<string, string> prefixes, bool writeDeclarations)
    {
        XamlTypeInfo type = _schema.GetType(node.Type) ?? throw new NotSupportedException($"Unknown type {node.Type}.");
        string name = Qualify(node.Type, prefixes);
        builder.Append('<').Append(name);
        if (writeDeclarations)
        {
            WriteDeclarations(builder, prefixes);
        }

        foreach (VAttribute attribute in node.AttributesArray)
        {
            if (attribute.Value is not string)
            {
                continue;
            }

            if (attribute.Name.Kind == XamlMemberKind.Directive)
            {
                if (attribute.Name.OwnerNamespace == XamlNamespaces.Xaml && attribute.Name.Member is "Name" or "Key")
                {
                    WriteAttribute(builder, attribute, prefixes);
                }

                continue;
            }

            if (type.ResolveMember(attribute.Name)?.Category == XamlMemberCategory.Event)
            {
                continue;
            }

            WriteAttribute(builder, attribute, prefixes);
        }

        bool textAsAttribute = !type.IsOpaque && node.ChildrenArray.Length > 0
            && type.ContentKind is XamlContentKind.Text or XamlContentKind.SingleObject
            && Array.TrueForAll(node.ChildrenArray, child => child.IsText);
        if (textAsAttribute)
        {
            string text = string.Concat(node.ChildrenArray.Select(child => child.Text));
            builder.Append(' ').Append(type.ContentMember!.Name).Append("=\"");
            AppendEscaped(builder, text.StartsWith('{') ? "{}" + text : text, isAttribute: true);
            builder.Append('"');
        }

        bool hasContent = node.PropertyElementsArray.Length > 0 || (!textAsAttribute && node.ChildrenArray.Length > 0);
        if (!hasContent)
        {
            builder.Append("/>");
            return;
        }

        builder.Append('>');
        foreach (VPropertyElement propertyElement in node.PropertyElementsArray)
        {
            XamlName owner = propertyElement.Member.Kind == XamlMemberKind.Attached ? propertyElement.Member.Owner : node.Type;
            string propertyName = $"{Qualify(owner, prefixes)}.{propertyElement.Member.Member}";
            builder.Append('<').Append(propertyName).Append('>');
            foreach (VNode value in propertyElement.ValuesArray)
            {
                if (value.IsText)
                {
                    AppendEscaped(builder, value.Text!, isAttribute: false);
                }
                else
                {
                    WriteElement(builder, value, prefixes, writeDeclarations: false);
                }
            }

            builder.Append("</").Append(propertyName).Append('>');
        }

        if (!textAsAttribute)
        {
            foreach (VNode child in node.ChildrenArray)
            {
                if (!child.IsText)
                {
                    WriteElement(builder, child, prefixes, writeDeclarations: false);
                }
                else if (type.ContentAcceptsRuns)
                {
                    builder.Append("<Run Text=\"");
                    AppendEscaped(builder, child.Text!, isAttribute: true);
                    builder.Append("\"/>");
                }
                else if (type.ContentAcceptsStrings)
                {
                    builder.Append("<x:String>");
                    AppendEscaped(builder, child.Text!, isAttribute: false);
                    builder.Append("</x:String>");
                }
                else
                {
                    throw new NotSupportedException($"Text content is not allowed in {node.Type.LocalName}.");
                }
            }
        }

        builder.Append("</").Append(name).Append('>');
    }

    private static void WriteAttribute(StringBuilder builder, VAttribute attribute, Dictionary<string, string> prefixes)
    {
        builder.Append(' ');
        switch (attribute.Name.Kind)
        {
            case XamlMemberKind.Directive:
                builder.Append(prefixes[attribute.Name.OwnerNamespace!]).Append(':').Append(attribute.Name.Member);
                break;
            case XamlMemberKind.Attached:
                builder.Append(Qualify(attribute.Name.Owner, prefixes)).Append('.').Append(attribute.Name.Member);
                break;
            default:
                builder.Append(attribute.Name.Member);
                break;
        }

        string value = (string)attribute.Value!;
        if (!attribute.IsMarkup && value.StartsWith('{'))
        {
            value = "{}" + value;
        }
        else if (attribute.IsMarkup && !MarkupExtensionSyntax.IsSafeForXamlReader(value))
        {
            throw new NotSupportedException($"{attribute.Name} uses an unsupported or malformed markup extension: {value}");
        }

        builder.Append("=\"");
        AppendEscaped(builder, value, isAttribute: true);
        builder.Append('"');
    }

    private static void WriteDeclarations(StringBuilder builder, Dictionary<string, string> prefixes)
    {
        foreach ((string uri, string prefix) in prefixes)
        {
            builder.Append(prefix.Length == 0 ? " xmlns" : $" xmlns:{prefix}").Append("=\"");
            AppendEscaped(builder, uri, isAttribute: true);
            builder.Append('"');
        }
    }

    private static void CollectNamespaces(VNode node, Dictionary<string, string> prefixes)
    {
        if (node.IsText)
        {
            return;
        }

        AddNamespace(node.Type.Namespace, prefixes);
        foreach (VAttribute attribute in node.AttributesArray)
        {
            if (attribute.Name.Kind == XamlMemberKind.Attached)
            {
                AddNamespace(attribute.Name.OwnerNamespace!, prefixes);
            }
        }

        foreach (VPropertyElement propertyElement in node.PropertyElementsArray)
        {
            if (propertyElement.Member.Kind == XamlMemberKind.Attached)
            {
                AddNamespace(propertyElement.Member.OwnerNamespace!, prefixes);
            }

            foreach (VNode value in propertyElement.ValuesArray)
            {
                CollectNamespaces(value, prefixes);
            }
        }

        foreach (VNode child in node.ChildrenArray)
        {
            CollectNamespaces(child, prefixes);
        }
    }

    private static void AddNamespace(string uri, Dictionary<string, string> prefixes)
    {
        if (uri == XamlNamespaces.VariableXaml)
        {
            throw new NotSupportedException("Virtual elements cannot be loaded by XamlReader.");
        }

        if (!prefixes.ContainsKey(uri))
        {
            prefixes[uri] = $"p{prefixes.Count}";
        }
    }

    private static string Qualify(XamlName name, Dictionary<string, string> prefixes)
    {
        string prefix = prefixes[name.Namespace];
        return prefix.Length == 0 ? name.LocalName : $"{prefix}:{name.LocalName}";
    }

    private static void AppendEscaped(StringBuilder builder, string text, bool isAttribute)
    {
        foreach (char c in text)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"' when isAttribute: builder.Append("&quot;"); break;
                case '\n' when isAttribute: builder.Append("&#10;"); break;
                case '\r' when isAttribute: builder.Append("&#13;"); break;
                case '\t' when isAttribute: builder.Append("&#9;"); break;
                default: builder.Append(c); break;
            }
        }
    }
}
