namespace Quill.Playground.VariableXaml.Model;

/// <summary>
/// One attribute of a virtual element.
/// </summary>
/// <param name="Name">The member the attribute targets.</param>
/// <param name="Value">
/// A string (converted with XAML type-converter rules, e.g. <c>"0,0,0,8"</c> becomes a Thickness),
/// or an already-typed value supplied by code (set as-is).
/// </param>
/// <param name="IsMarkup">
/// True when the string came from XAML markup, so <c>{...}</c> denotes a markup extension such as
/// <c>{ThemeResource Key}</c> and <c>{}</c> escapes a literal brace. Strings set from code are literal.
/// </param>
public readonly record struct VAttribute(XamlMemberName Name, object? Value, bool IsMarkup = false)
{
    public override string ToString() => $"{Name}=\"{Value}\"";
}
