using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Quill.VariableXaml.Metadata;
using Quill.VariableXaml.Model;

namespace Quill.VariableXaml.Reconciliation;

/// <summary>Attributes: diffing, value conversion, markup extensions, resources and events.</summary>
internal sealed partial class Reconciler
{
    private void PatchAttributes(LiveNode live, VNode previous, VNode next)
    {
        VAttribute[] oldAttributes = previous.AttributesArray;
        VAttribute[] newAttributes = next.AttributesArray;
        for (int i = 0; i < newAttributes.Length; i++)
        {
            int oldIndex = IndexOfAttribute(oldAttributes, newAttributes[i].Name, i);
            if (oldIndex < 0 || oldAttributes[oldIndex] != newAttributes[i])
            {
                ApplyAttribute(live, newAttributes[i]);
            }
        }

        foreach (VAttribute oldAttribute in oldAttributes)
        {
            if (IndexOfAttribute(newAttributes, oldAttribute.Name, 0) < 0)
            {
                ClearAttribute(live, oldAttribute);
            }
        }
    }

    internal void ApplyAttribute(LiveNode live, VAttribute attribute)
    {
        object target = live.Instance!;
        XamlMemberName name = attribute.Name;
        if (name.Kind == XamlMemberKind.Directive)
        {
            // vx:Key, x:Key, x:Uid, x:Load... only matter to reconciliation or to XamlReader.
            if (name.IsDirective(XamlNamespaces.Xaml, "Name") && target is FrameworkElement element)
            {
                element.Name = attribute.Value?.ToString() ?? string.Empty;
                Statistics.PropertiesSet++;
            }

            return;
        }

        XamlMemberInfo? member = live.Type?.ResolveMember(name);
        if (member is null)
        {
            Report($"<{live.Node.Type.LocalName}> has no member '{name}'.");
            return;
        }

        try
        {
            if (member.Category == XamlMemberCategory.Event)
            {
                AttachEvent(live, name, member, attribute.Value);
                return;
            }

            if (attribute.Value is string text && attribute.IsMarkup && MarkupExtensionSyntax.TryParse(text, out MarkupExtensionSyntax? extension))
            {
                ApplyMarkupExtension(live, member, attribute, extension!);
            }
            else
            {
                object? value = attribute.Value is string literal
                    ? _converter.FromString(member.ValueType, attribute.IsMarkup ? MarkupExtensionSyntax.Unescape(literal) : literal)
                    : attribute.Value;
                member.SetValue(target, value);
            }

            Statistics.PropertiesSet++;
        }
        catch (Exception exception)
        {
            Report($"{live.Node.Type.LocalName}.{name} = \"{attribute.Value}\" failed: {FirstLine(exception.Message)}");

            // Keep the live tree a function of the markup alone: a value that cannot be applied
            // leaves the member at its default, exactly as a fresh mount of the same markup would,
            // instead of silently keeping the previous value.
            try
            {
                member.ClearValue(target);
            }
            catch (Exception)
            {
                // Nothing more to restore.
            }
        }
    }

    internal void ClearAttribute(LiveNode live, VAttribute attribute)
    {
        XamlMemberName name = attribute.Name;
        if (name.Kind == XamlMemberKind.Directive)
        {
            if (name.IsDirective(XamlNamespaces.Xaml, "Name") && live.Instance is FrameworkElement element)
            {
                element.Name = string.Empty;
            }

            return;
        }

        XamlMemberInfo? member = live.Type?.ResolveMember(name);
        if (member is null)
        {
            return;
        }

        try
        {
            if (member.Category == XamlMemberCategory.Event)
            {
                DetachEvent(live, name, member);
                return;
            }

            member.ClearValue(live.Instance!);
            Statistics.PropertiesCleared++;
        }
        catch (Exception exception)
        {
            Report($"Clearing {live.Node.Type.LocalName}.{name} failed: {FirstLine(exception.Message)}");
        }
    }

    private void ApplyMarkupExtension(LiveNode live, XamlMemberInfo member, VAttribute attribute, MarkupExtensionSyntax extension)
    {
        object target = live.Instance!;
        switch (extension.Name)
        {
            case "x:Null":
                member.SetValue(target, null);
                break;
            case "StaticResource":
            case "ThemeResource":
                string key = extension.PositionalArgument ?? extension.NamedArguments.GetValueOrDefault("ResourceKey")
                    ?? throw new FormatException($"{extension.Name} needs a resource key.");
                member.SetValue(target, FindResource(live, key));
                break;
            case "Binding" when member.DependencyProperty is not null && target is DependencyObject dependencyObject:
                BindingOperations.SetBinding(dependencyObject, member.DependencyProperty, CreateBinding(extension));
                break;
            default:
                // Other well-formed extensions (CustomResource...): let XamlReader evaluate a
                // one-attribute probe. Malformed ones never reach XamlReader (native crash risk).
                if (!MarkupExtensionSyntax.IsSafeForXamlReader((string)attribute.Value!))
                {
                    throw new FormatException($"Unsupported or malformed markup extension '{extension.Name}'.");
                }

                Statistics.XamlReaderLoads++;
                object probe = XamlReader.Load(_writer.WriteProbe(live.Node.Type, attribute));
                member.SetValue(target, member.GetValue(probe));
                break;
        }
    }

    /// <summary>
    /// Resolves a resource key like XAML does: dynamic ancestors' Resources, then the host's
    /// visual ancestors, then the application (which includes theme dictionaries).
    /// </summary>
    /// <remarks>
    /// Values resolved here are snapshots: unlike elements created by XamlReader, a ThemeResource
    /// applied by a patch does not follow later theme changes.
    /// </remarks>
    private object? FindResource(LiveNode live, string key)
    {
        for (LiveNode? node = live; node is not null; node = node.Parent)
        {
            if (node.Instance is FrameworkElement element && HasResourcesElement(node.Node) && element.Resources.TryGetValue(key, out object? value))
            {
                return value;
            }
        }

        for (DependencyObject? current = _host; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is FrameworkElement element && element.Resources.TryGetValue(key, out object? value))
            {
                return value;
            }
        }

        if (Application.Current.Resources.TryGetValue(key, out object? applicationValue))
        {
            return applicationValue;
        }

        throw new KeyNotFoundException($"Resource '{key}' was not found.");
    }

    private static bool HasResourcesElement(VNode node) =>
        Array.Exists(node.PropertyElementsArray, element => element.Member.Member == nameof(FrameworkElement.Resources));

    private static Binding CreateBinding(MarkupExtensionSyntax extension)
    {
        var binding = new Binding();
        string? path = extension.PositionalArgument ?? extension.NamedArguments.GetValueOrDefault("Path");
        if (path is not null)
        {
            binding.Path = new PropertyPath(path);
        }

        if (extension.NamedArguments.TryGetValue("Mode", out string? mode))
        {
            binding.Mode = Enum.Parse<BindingMode>(mode);
        }

        return binding;
    }

    private void AttachEvent(LiveNode live, XamlMemberName name, XamlMemberInfo member, object? value)
    {
        EventInfo eventInfo = member.Event!;
        Type handlerType = eventInfo.EventHandlerType!;
        Delegate handler = value switch
        {
            Delegate candidate when handlerType.IsInstanceOfType(candidate) => candidate,
            string handlerName => _eventBinder.GetHandler(handlerType, handlerName),
            _ => throw new ArgumentException($"expected a handler name or a {handlerType.Name} delegate"),
        };

        if (live.Events?.TryGetValue(name, out Delegate? existing) == true && ReferenceEquals(existing, handler))
        {
            return;
        }

        DetachEvent(live, name, member);
        eventInfo.AddEventHandler(live.Instance, handler);
        (live.Events ??= [])[name] = handler;
    }

    private static void DetachEvent(LiveNode live, XamlMemberName name, XamlMemberInfo member)
    {
        if (live.Events is not null && live.Events.Remove(name, out Delegate? previous))
        {
            member.Event!.RemoveEventHandler(live.Instance, previous);
        }
    }

    private static int IndexOfAttribute(VAttribute[] attributes, XamlMemberName name, int hint)
    {
        if (hint < attributes.Length && attributes[hint].Name == name)
        {
            return hint;
        }

        for (int i = 0; i < attributes.Length; i++)
        {
            if (attributes[i].Name == name)
            {
                return i;
            }
        }

        return -1;
    }
}
