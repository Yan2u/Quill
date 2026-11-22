namespace Quill.VariableXaml.Model;

/// <summary>
/// Well-known XML namespaces understood by the VariableXaml markup pipeline.
/// </summary>
public static class XamlNamespaces
{
    /// <summary>The default WinUI presentation namespace.</summary>
    public const string Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    /// <summary>The XAML language namespace (conventionally mapped to the <c>x:</c> prefix).</summary>
    public const string Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    /// <summary>The markup-compatibility namespace (conventionally <c>mc:</c>).</summary>
    public const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The designer namespace (conventionally <c>d:</c>); always ignored.</summary>
    public const string Designer = "http://schemas.microsoft.com/expression/blend/2008";

    /// <summary>
    /// Directives and virtual elements owned by VariableXamlRoot (conventionally <c>vx:</c>),
    /// for example <c>vx:Key</c> and <c>vx:VirtualStack</c>.
    /// </summary>
    public const string VariableXaml = "urn:quill:variable-xaml";

    /// <summary>The <c>xml:</c> namespace, implicitly declared in every document.</summary>
    public const string Xml = "http://www.w3.org/XML/1998/namespace";
}
