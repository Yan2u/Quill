using System.Globalization;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Localization;
using Windows.UI;

namespace Quill.Editor.WinUI.Theming;

/// <summary>
/// One style slot of <see cref="MdStyleSheet"/> as themes store and the configuration page edit it (15-themes.md):
/// its name (the key in a theme file), group, display name, kind and range, and how its value is written as a
/// string and read back. Reading a value that is not valid gives null, and the theme keeps the slot as it was.
/// </summary>
public sealed class StyleSlot
{
    private readonly string _defaultDisplayName;
    private readonly Func<MdStyleSheet, string> _get;
    private readonly Func<MdStyleSheet, string, MdStyleSheet?> _set;

    internal StyleSlot(string name, string displayName, ThemeGroup group, StyleSlotKind kind, Func<MdStyleSheet, string> get, Func<MdStyleSheet, string, MdStyleSheet?> set)
    {
        Name = name;
        _defaultDisplayName = displayName;
        Group = group;
        Kind = kind;
        _get = get;
        _set = set;
    }

    public string Name { get; }

    public string DisplayName
    {
        get
        {
            string key = $"theme.slot.{Name}";
            string localized = UiStrings.Get(key);
            return ReferenceEquals(localized, key) ? _defaultDisplayName : localized;
        }
    }

    public ThemeGroup Group { get; }

    public StyleSlotKind Kind { get; }

    public double Minimum { get; init; }

    public double Maximum { get; init; } = double.MaxValue;

    /// <summary>The values of a <see cref="StyleSlotKind.Choice"/> and their labels.</summary>
    public IReadOnlyList<(string Value, string Label)> Options { get; init; } = [];

    /// <summary>The slot's value in <paramref name="sheet"/>, as stored.</summary>
    public string Get(MdStyleSheet sheet) => _get(sheet);

    /// <summary>The sheet with the slot set to <paramref name="value"/>, or null when the value is not valid for the slot.</summary>
    public MdStyleSheet? Set(MdStyleSheet sheet, string value) => _set(sheet, value.Trim());

    // Values as strings (Q112): colors #RRGGBB or #AARRGGBB, invariant numbers, paddings "left,top,right,bottom".

    public static string FormatColor(Color color) => color.A == 0xFF
        ? $"#{color.R:X2}{color.G:X2}{color.B:X2}"
        : $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static Color? ParseColor(string text)
    {
        string hex = text.Trim().TrimStart('#');
        if (hex.Length is not (6 or 8) || !uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
        {
            return null;
        }

        byte alpha = hex.Length == 8 ? (byte)(value >> 24) : (byte)0xFF;
        return Color.FromArgb(alpha, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
    }

    public static string FormatNumber(double value) => Math.Round(value, 4).ToString("0.####", CultureInfo.InvariantCulture);

    public static double? ParseNumber(string text) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;

    public static string FormatPadding(Thickness padding) =>
        string.Join(",", new[] { padding.Left, padding.Top, padding.Right, padding.Bottom }.Select(FormatNumber));

    public static Thickness? ParsePadding(string text, double max = 500)
    {
        var parts = text.Split(',').Select(static part => ParseNumber(part.Trim())).ToList();
        return parts.Count == 4 && parts.All(part => part >= 0 && part <= max)
            ? new Thickness(parts[0]!.Value, parts[1]!.Value, parts[2]!.Value, parts[3]!.Value)
            : parts.Count == 1 && parts[0] >= 0 && parts[0] <= max ? new Thickness(parts[0]!.Value) : null;
    }
}
