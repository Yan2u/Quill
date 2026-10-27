using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace Quill.Playground.VariableXaml.Metadata;

/// <summary>
/// Converts attribute strings to typed values with the XAML type converters
/// (<see cref="XamlBindingHelper.ConvertValue"/>), caching immutable results.
/// </summary>
/// <remarks>
/// Probe results (see SelfTest/PlatformProbes): ConvertValue handles primitives, enums, Thickness,
/// CornerRadius, GridLength, Color, Brush, FontWeight, FontFamily, Geometry path data,
/// ImageSource URIs, Duration and more at roughly 4 us per call, while
/// <c>XamlBindingHelper.SetPropertyFromString</c> rejects every non-string property in WinUI 3.
/// </remarks>
internal sealed class ValueConverter
{
    private const int MaxCacheEntries = 4096;

    private readonly Dictionary<(Type Type, string Text), object?> _cache = [];

    public object? FromString(Type targetType, string text)
    {
        if (targetType == typeof(string) || targetType == typeof(object))
        {
            return text;
        }

        if (_cache.TryGetValue((targetType, text), out object? cached))
        {
            return cached;
        }

        object? value = XamlBindingHelper.ConvertValue(targetType, text);
        if (IsShareable(targetType, value))
        {
            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[(targetType, text)] = value;
        }

        return value;
    }

    /// <summary>
    /// Values that are safe to share between elements: value types and objects nothing mutates.
    /// Geometry and ImageSource instances are not shared (an element may own them).
    /// </summary>
    private static bool IsShareable(Type targetType, object? value) =>
        targetType.IsValueType || value is FontFamily or Uri or SolidColorBrush;
}
