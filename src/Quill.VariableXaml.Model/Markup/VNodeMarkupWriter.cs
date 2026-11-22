using System.Globalization;
using System.Text;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Markup;

/// <summary>
/// Writes a virtual tree as indented XAML-like text for snapshots, logs and diffs (08-testing-and-
/// debugging.md). Unlike the reconciler's writer it needs no type metadata: typed values are written
/// with <see cref="IFormattable"/> or <see cref="object.ToString"/>, or with a caller-supplied formatter,
/// so the output is for reading, not for <c>XamlReader</c>.
/// </summary>
public static class VNodeMarkupWriter
{
    public static string Write(VNode root, Func<object, string?>? formatValue = null)
    {
        var builder = new StringBuilder();
        WriteNode(builder, root, 0, formatValue);
        return builder.ToString();
    }

    private static void WriteNode(StringBuilder builder, VNode node, int depth, Func<object, string?>? formatValue)
    {
        builder.Append(' ', depth * 2);
        if (node.IsText)
        {
            builder.Append('"').Append(Escape(node.Text!)).Append('"').Append('\n');
            return;
        }

        string name = Name(node.Type);
        builder.Append('<').Append(name);
        foreach (VAttribute attribute in node.AttributesArray)
        {
            builder.Append(' ').Append(attribute.Name.ToString()).Append("=\"").Append(Escape(Format(attribute.Value, formatValue))).Append('"');
        }

        if (node.ChildrenArray.Length == 0 && node.PropertyElementsArray.Length == 0)
        {
            builder.Append(" />\n");
            return;
        }

        builder.Append(">\n");
        foreach (VPropertyElement property in node.PropertyElementsArray)
        {
            builder.Append(' ', depth * 2 + 2).Append('<').Append(name).Append('.').Append(property.Member.Member).Append(">\n");
            foreach (VNode value in property.ValuesArray)
            {
                WriteNode(builder, value, depth + 2, formatValue);
            }

            builder.Append(' ', depth * 2 + 2).Append("</").Append(name).Append('.').Append(property.Member.Member).Append(">\n");
        }

        foreach (VNode child in node.ChildrenArray)
        {
            WriteNode(builder, child, depth + 1, formatValue);
        }

        builder.Append(' ', depth * 2).Append("</").Append(name).Append(">\n");
    }

    private static string Name(XamlName type) => type.Namespace switch
    {
        XamlNamespaces.Presentation => type.LocalName,
        XamlNamespaces.VariableXaml => "vx:" + type.LocalName,
        _ => type.LocalName,
    };

    private static string Format(object? value, Func<object, string?>? formatValue) => value switch
    {
        null => "{x:Null}",
        string text => text,
        bool flag => flag ? "True" : "False",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => formatValue?.Invoke(value) ?? value.ToString() ?? string.Empty,
    };

    private static string Escape(string text) =>
        text.Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace("\n", "&#10;", StringComparison.Ordinal);
}
