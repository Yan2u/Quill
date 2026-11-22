using System.Reflection;
using Microsoft.UI.Xaml;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Metadata;

/// <summary>
/// Resolves XAML type names to CLR types and caches their metadata.
/// </summary>
/// <remarks>
/// The presentation namespace maps to the Microsoft.UI.Xaml.* CLR namespaces of Microsoft.WinUI.
/// <c>using:Ns</c> and <c>clr-namespace:Ns[;assembly=A]</c> namespaces map to CLR namespaces in the
/// loaded or referenced assemblies (e.g. CommunityToolkit, DevWinUI). All access is UI-thread only.
/// </remarks>
public sealed class XamlSchema
{
    private static readonly string[] PresentationClrNamespaces =
    [
        "Microsoft.UI.Xaml.Controls",
        "Microsoft.UI.Xaml.Documents",
        "Microsoft.UI.Xaml.Shapes",
        "Microsoft.UI.Xaml.Media",
        "Microsoft.UI.Xaml.Media.Imaging",
        "Microsoft.UI.Xaml.Media.Animation",
        "Microsoft.UI.Xaml.Media.Media3D",
        "Microsoft.UI.Xaml.Controls.Primitives",
        "Microsoft.UI.Xaml",
        "Microsoft.UI.Xaml.Input",
        "Microsoft.UI.Xaml.Data",
        "Microsoft.UI.Xaml.Automation",
        "Microsoft.UI.Xaml.Navigation",
    ];

    private readonly Dictionary<XamlName, XamlTypeInfo?> _types = [];
    private readonly Dictionary<XamlName, ElementRegistration> _registrations = [];
    private Dictionary<string, Type>? _presentationTypes;

    public static XamlSchema Default { get; } = new();

    /// <summary>
    /// Registers an element type created in code (typed factory and setters). It replaces any
    /// reflection-based resolution of the same name.
    /// </summary>
    public void Register(ElementRegistration registration)
    {
        _registrations[registration.Name] = registration;
        _types[registration.Name] = new XamlTypeInfo(this, registration);
    }

    public bool IsRegistered(XamlName name) => _registrations.ContainsKey(name);

    public XamlTypeInfo? GetType(XamlName name)
    {
        if (_types.TryGetValue(name, out XamlTypeInfo? info))
        {
            return info;
        }

        Type? clrType = ResolveClrType(name);
        info = clrType is null ? null : new XamlTypeInfo(this, name, clrType);
        _types[name] = info;
        return info;
    }

    public bool IsWhitespaceSignificant(XamlName name) => GetType(name)?.IsWhitespaceSignificant ?? false;

    /// <summary>Returns the XAML name for a CLR type in the presentation namespace, if it lives there.</summary>
    public static bool IsPresentationType(Type type) =>
        type.Namespace is not null && Array.IndexOf(PresentationClrNamespaces, type.Namespace) >= 0 && type.Assembly == typeof(UIElement).Assembly;

    private Type? ResolveClrType(XamlName name)
    {
        if (name.Namespace == XamlNamespaces.Presentation)
        {
            _presentationTypes ??= BuildPresentationTypeMap();
            return _presentationTypes.GetValueOrDefault(name.LocalName);
        }

        string? clrNamespace = null;
        string? assemblyName = null;
        if (name.Namespace.StartsWith("using:", StringComparison.Ordinal))
        {
            clrNamespace = name.Namespace["using:".Length..];
        }
        else if (name.Namespace.StartsWith("clr-namespace:", StringComparison.Ordinal))
        {
            string[] parts = name.Namespace["clr-namespace:".Length..].Split(';');
            clrNamespace = parts[0];
            assemblyName = parts.Skip(1).FirstOrDefault(part => part.StartsWith("assembly=", StringComparison.Ordinal))?["assembly=".Length..];
        }

        return clrNamespace is null ? null : FindClrType($"{clrNamespace}.{name.LocalName}", assemblyName);
    }

    private static Dictionary<string, Type> BuildPresentationTypeMap()
    {
        var map = new Dictionary<string, Type>(StringComparer.Ordinal);
        var byNamespace = typeof(UIElement).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsGenericType && type.Namespace is not null)
            .ToLookup(type => type.Namespace!);

        // Earlier namespaces win on name clashes, mirroring the XAML compiler's precedence for common types.
        foreach (string clrNamespace in PresentationClrNamespaces)
        {
            foreach (Type type in byNamespace[clrNamespace])
            {
                map.TryAdd(type.Name, type);
            }
        }

        return map;
    }

    private static Type? FindClrType(string fullName, string? assemblyName)
    {
        if (assemblyName is not null)
        {
            return Type.GetType($"{fullName}, {assemblyName}", throwOnError: false);
        }

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
            {
                return type;
            }
        }

        // The assembly may be referenced but not loaded yet (e.g. a toolkit control used for the first time).
        // Probe the app directory for assemblies sharing the namespace's first two segments.
        string[] segments = fullName.Split('.');
        string filePrefix = segments.Length > 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
        foreach (string path in Directory.EnumerateFiles(AppContext.BaseDirectory, filePrefix + "*.dll"))
        {
            try
            {
                Type? type = Assembly.LoadFrom(path).GetType(fullName, throwOnError: false);
                if (type is not null)
                {
                    return type;
                }
            }
            catch (Exception exception) when (exception is FileNotFoundException or FileLoadException or BadImageFormatException)
            {
                // Not a loadable managed assembly; keep looking.
            }
        }

        return null;
    }
}
