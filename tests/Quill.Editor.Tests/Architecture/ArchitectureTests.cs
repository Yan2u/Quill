using System.Reflection;

namespace Quill.Editor.Tests.Architecture;

/// <summary>
/// Guards the dependency rules of 01-architecture.md: the Document Adapter must not reference UI
/// frameworks, and Markdig types must not leak into its public API.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly EditorAssembly = Assembly.Load(new AssemblyName("Quill.Editor"));

    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Microsoft.UI",
        "Microsoft.WindowsAppRuntime",
        "Microsoft.WindowsAppSDK",
        "Microsoft.Windows.SDK.NET",
        "Microsoft.Windows.ApplicationModel",
        "WinRT.Runtime",
        "Windows.",
        "System.Windows",
        "PresentationCore",
        "PresentationFramework",
        "WindowsBase",
    ];

    [Fact]
    public void EditorAssemblyDoesNotReferenceUiFrameworks()
    {
        var offending = EditorAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.Empty(offending);
    }

    [Fact]
    public void PublicApiDoesNotExposeMarkdigTypes()
    {
        var leaks = new List<string>();
        foreach (var type in EditorAssembly.GetExportedTypes())
        {
            foreach (var (description, referenced) in PublicSurface(type))
            {
                if (ReferencesMarkdig(referenced))
                {
                    leaks.Add($"{type.FullName}: {description} uses {referenced}");
                }
            }
        }

        Assert.Empty(leaks);
    }

    private static IEnumerable<(string Description, Type Referenced)> PublicSurface(Type type)
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        if (type.BaseType is { } baseType)
        {
            yield return ("base type", baseType);
        }

        foreach (var implemented in type.GetInterfaces())
        {
            yield return ("interface", implemented);
        }

        foreach (var field in type.GetFields(Members).Where(field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
        {
            yield return ($"field {field.Name}", field.FieldType);
        }

        foreach (var method in type.GetMethods(Members).Where(IsVisible))
        {
            yield return ($"method {method.Name} return", method.ReturnType);
            foreach (var parameter in method.GetParameters())
            {
                yield return ($"method {method.Name} parameter {parameter.Name}", parameter.ParameterType);
            }
        }

        foreach (var constructor in type.GetConstructors(Members).Where(IsVisible))
        {
            foreach (var parameter in constructor.GetParameters())
            {
                yield return ($"constructor parameter {parameter.Name}", parameter.ParameterType);
            }
        }

        foreach (var property in type.GetProperties(Members))
        {
            var accessors = property.GetAccessors(nonPublic: true);
            if (accessors.Any(IsVisible))
            {
                yield return ($"property {property.Name}", property.PropertyType);
            }
        }

        foreach (var eventInfo in type.GetEvents(Members))
        {
            if (eventInfo.AddMethod is { } add && IsVisible(add) && eventInfo.EventHandlerType is { } handler)
            {
                yield return ($"event {eventInfo.Name}", handler);
            }
        }
    }

    private static bool IsVisible(MethodBase method) => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;

    private static bool ReferencesMarkdig(Type type)
    {
        if (type.HasElementType)
        {
            return ReferencesMarkdig(type.GetElementType()!);
        }

        if (type.IsGenericType && type.GetGenericArguments().Any(ReferencesMarkdig))
        {
            return true;
        }

        return !type.IsGenericParameter && type.Assembly.GetName().Name == "Markdig";
    }
}
