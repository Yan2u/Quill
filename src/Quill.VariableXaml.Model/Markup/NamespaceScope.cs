using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Markup;

/// <summary>
/// Immutable chain of in-scope XML namespace prefixes, ignorable namespaces and xml:space state.
/// </summary>
/// <remarks>
/// The root scope is lenient: the default namespace and the <c>x</c>, <c>vx</c>, <c>mc</c> and <c>d</c>
/// prefixes are predeclared so playground markup can omit the usual xmlns boilerplate.
/// Explicit declarations in the markup override these defaults.
/// </remarks>
internal sealed class NamespaceScope
{
    public static readonly NamespaceScope Default = new(
        null,
        new Dictionary<string, string>
        {
            [string.Empty] = XamlNamespaces.Presentation,
            ["x"] = XamlNamespaces.Xaml,
            ["vx"] = XamlNamespaces.VariableXaml,
            ["mc"] = XamlNamespaces.MarkupCompatibility,
            ["d"] = XamlNamespaces.Designer,
            ["xml"] = XamlNamespaces.Xml,
        },
        ignorableNamespaces: null,
        preserveSpace: false);

    private readonly NamespaceScope? _parent;
    private readonly Dictionary<string, string>? _prefixes;
    private readonly HashSet<string>? _ignorableNamespaces;

    private NamespaceScope(NamespaceScope? parent, Dictionary<string, string>? prefixes, HashSet<string>? ignorableNamespaces, bool preserveSpace)
    {
        _parent = parent;
        _prefixes = prefixes;
        _ignorableNamespaces = ignorableNamespaces;
        PreserveSpace = preserveSpace;
    }

    public bool PreserveSpace { get; }

    public NamespaceScope Push(Dictionary<string, string>? prefixes, HashSet<string>? ignorableNamespaces, bool? preserveSpace)
    {
        if (prefixes is null && ignorableNamespaces is null && (preserveSpace is null || preserveSpace == PreserveSpace))
        {
            return this;
        }

        return new NamespaceScope(this, prefixes, ignorableNamespaces, preserveSpace ?? PreserveSpace);
    }

    public string? LookupNamespace(string prefix)
    {
        for (NamespaceScope? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._prefixes is not null && scope._prefixes.TryGetValue(prefix, out string? uri))
            {
                return uri;
            }
        }

        return null;
    }

    public bool IsIgnorable(string namespaceUri)
    {
        if (namespaceUri == XamlNamespaces.Designer || namespaceUri == XamlNamespaces.MarkupCompatibility)
        {
            return true;
        }

        for (NamespaceScope? scope = this; scope is not null; scope = scope._parent)
        {
            if (scope._ignorableNamespaces?.Contains(namespaceUri) == true)
            {
                return true;
            }
        }

        return false;
    }
}
