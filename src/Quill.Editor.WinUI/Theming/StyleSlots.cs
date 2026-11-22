using Quill.Editor.WinUI.Localization;
using Quill.Highlighting;
using Windows.UI;

namespace Quill.Editor.WinUI.Theming;

/// <summary>
/// The table of style slots (15-themes.md): every slot of <see cref="MdStyleSheet"/> that a theme sets, with its
/// group and display name. Theme files, the configuration page's form and its search are made from it. The
/// heading sizes are six slots, <c>HeadingScale1</c> to <c>HeadingScale6</c>.
/// </summary>
public static class StyleSlots
{
    public static IReadOnlyList<StyleSlot> All { get; } = Create();

    private static readonly Dictionary<string, StyleSlot> ByName = All.ToDictionary(static slot => slot.Name, StringComparer.Ordinal);

    /// <summary>The slot named <paramref name="name"/> (as in a theme file), or null.</summary>
    public static StyleSlot? Find(string name) => ByName.GetValueOrDefault(name);

    /// <summary>The display name of a group.</summary>
    public static string Title(ThemeGroup group) => UiStrings.Get($"theme.group.{group}");

    /// <summary>The display label of a choice slot option.</summary>
    public static string OptionLabel(string value, string fallback) => value switch
    {
        "400" or "500" or "600" or "700" or "800" or "900" => UiStrings.Get($"theme.option.{value}"),
        _ => fallback,
    };

    /// <summary>A sheet with the stored values applied over <paramref name="basis"/>; unknown names and values that are not valid are left out.</summary>
    public static MdStyleSheet Apply(MdStyleSheet basis, IReadOnlyDictionary<string, string> values)
    {
        var sheet = basis;
        foreach (var (name, value) in values)
        {
            if (Find(name)?.Set(sheet, value) is { } changed)
            {
                sheet = changed;
            }
        }

        return sheet;
    }

    /// <summary>The slots of <paramref name="sheet"/> that differ from <paramref name="basis"/>, as stored.</summary>
    public static Dictionary<string, string> Differences(MdStyleSheet basis, MdStyleSheet sheet)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var slot in All)
        {
            string value = slot.Get(sheet);
            if (value != slot.Get(basis))
            {
                values[slot.Name] = value;
            }
        }

        return values;
    }

    private static IReadOnlyList<StyleSlot> Create()
    {
        const ThemeGroup page = ThemeGroup.PageAndText;
        const ThemeGroup headings = ThemeGroup.Headings;
        const ThemeGroup inline = ThemeGroup.LinksAndInline;
        const ThemeGroup code = ThemeGroup.Code;
        const ThemeGroup other = ThemeGroup.RulesAndOther;
        var slots = new List<StyleSlot>
        {
            // Page and text.
            Paint("PageBackground", "Page background", page, static s => s.PageBackground, static (s, v) => s with { PageBackground = v }),
            Paint("Foreground", "Text color", page, static s => s.Foreground, static (s, v) => s with { Foreground = v }),
            Paint("SecondaryForeground", "Markers and decorations", page, static s => s.SecondaryForeground, static (s, v) => s with { SecondaryForeground = v }),
            Font("FontFamily", "Font", page, static s => s.FontFamily, static (s, v) => s with { FontFamily = v }),
            Length("FontSize", "Font size", page, static s => s.FontSize, static (s, v) => s with { FontSize = (float)v }, 8, 48),
            Scale("LineHeight", "Line height", page, static s => s.LineHeight, static (s, v) => s with { LineHeight = (float)v }, 1, 3),
            Length("ContentMaxWidth", "Content width", page, static s => s.ContentMaxWidth, static (s, v) => s with { ContentMaxWidth = v }, 300, 3000),
            Length("PagePaddingHorizontal", "Page margin, left and right", page, static s => s.PagePaddingHorizontal, static (s, v) => s with { PagePaddingHorizontal = v }, 0, 300),
            Length("PagePaddingVertical", "Page margin, top and bottom", page, static s => s.PagePaddingVertical, static (s, v) => s with { PagePaddingVertical = v }, 0, 300),
            Length("BlockSpacing", "Paragraph spacing", page, static s => s.BlockSpacing, static (s, v) => s with { BlockSpacing = v }, 0, 100),
            Length("IncrementalTabStop", "Tab width", page, static s => s.IncrementalTabStop, static (s, v) => s with { IncrementalTabStop = (float)v }, 4, 200),
            Paint("SelectionBackground", "Selection", page, static s => s.SelectionBackground, static (s, v) => s with { SelectionBackground = v }),
            Optional("SelectionForeground", "Selected text", page, static s => s.SelectionForeground, static (s, v) => s with { SelectionForeground = v }),
            Paint("SearchMatchBackground", "Find matches", page, static s => s.SearchMatchBackground, static (s, v) => s with { SearchMatchBackground = v }),
            Paint("SearchCurrentBackground", "Current find match", page, static s => s.SearchCurrentBackground, static (s, v) => s with { SearchCurrentBackground = v }),
            Paint("CompositionUnderline", "Input method underline", page, static s => s.CompositionUnderline, static (s, v) => s with { CompositionUnderline = v }),
            Paint("BlockCaret", "Block caret", page, static s => s.BlockCaret, static (s, v) => s with { BlockCaret = v }),
            Paint("BlockSelection", "Selected objects", page, static s => s.BlockSelection, static (s, v) => s with { BlockSelection = v }),

            // Headings.
            Paint("HeadingForeground", "Heading color", headings, static s => s.HeadingForeground, static (s, v) => s with { HeadingForeground = v }),
            Paint("Heading6Foreground", "Heading 6 color", headings, static s => s.Heading6Foreground, static (s, v) => s with { Heading6Foreground = v }),
            Choice("BlockBoldWeight", "Heading weight", headings,
                [("400", "Normal"), ("500", "Medium"), ("600", "Semibold"), ("700", "Bold"), ("800", "Extra bold"), ("900", "Black")],
                static s => s.BlockBoldWeight.ToString(System.Globalization.CultureInfo.InvariantCulture),
                static (s, v) => ushort.TryParse(v, out ushort weight) && weight is >= 100 and <= 950 ? s with { BlockBoldWeight = weight } : null),
        };

        for (int level = 1; level <= 6; level++)
        {
            int index = level - 1;
            slots.Add(Scale($"HeadingScale{level}", $"Heading {level} size", headings, s => s.HeadingScales[index], (s, v) => s with { HeadingScales = [.. s.HeadingScales.Select((scale, i) => i == index ? (float)v : scale)] }, 0.5, 5));
        }

        slots.AddRange(
        [
            Scale("HeadingLineHeight", "Heading line height", headings, static s => s.HeadingLineHeight, static (s, v) => s with { HeadingLineHeight = (float)v }, 1, 3),
            Length("HeadingSpacing", "Heading spacing", headings, static s => s.HeadingSpacing, static (s, v) => s with { HeadingSpacing = v }, 0, 100),
            Paint("HeadingRule", "Heading rule", headings, static s => s.HeadingRule, static (s, v) => s with { HeadingRule = v }),
            Length("HeadingRuleThickness", "Heading rule thickness", headings, static s => s.HeadingRuleThickness, static (s, v) => s with { HeadingRuleThickness = v }, 0, 20),
            Integer("HeadingRuleLevels", "Headings with a rule (levels 1 to)", headings, static s => s.HeadingRuleLevels, static (s, v) => s with { HeadingRuleLevels = v }, 0, 6),
            Length("HeadingRuleGap", "Heading rule gap", headings, static s => s.HeadingRuleGap, static (s, v) => s with { HeadingRuleGap = v }, 0, 50),

            // Links and inline elements.
            Paint("LinkForeground", "Link color", inline, static s => s.LinkForeground, static (s, v) => s with { LinkForeground = v }),
            Toggle("LinkUnderline", "Underline links", inline, static s => s.LinkUnderline, static (s, v) => s with { LinkUnderline = v }),
            Paint("HighlightBackground", "Highlight", inline, static s => s.HighlightBackground, static (s, v) => s with { HighlightBackground = v }),
            Optional("HighlightForeground", "Highlighted text", inline, static s => s.HighlightForeground, static (s, v) => s with { HighlightForeground = v }),
            Paint("KeyboardForeground", "Keyboard text", inline, static s => s.KeyboardForeground, static (s, v) => s with { KeyboardForeground = v }),
            Paint("KeyboardBackground", "Keyboard background", inline, static s => s.KeyboardBackground, static (s, v) => s with { KeyboardBackground = v }),
            Paint("KeyboardBorder", "Keyboard border", inline, static s => s.KeyboardBorder, static (s, v) => s with { KeyboardBorder = v }),
            Length("InlineCornerRadius", "Inline box corner radius", inline, static s => s.InlineCornerRadius, static (s, v) => s with { InlineCornerRadius = (float)v }, 0, 20),
            Scale("ScriptScale", "Superscript and subscript size", inline, static s => s.ScriptScale, static (s, v) => s with { ScriptScale = (float)v }, 0.3, 1.5),
            Paint("FootnoteForeground", "Footnote reference", inline, static s => s.FootnoteForeground, static (s, v) => s with { FootnoteForeground = v }),
            Toggle("FootnoteBold", "Bold footnote references", inline, static s => s.FootnoteBold, static (s, v) => s with { FootnoteBold = v }),
            Paint("FootnoteBackground", "Footnote reference background", inline, static s => s.FootnoteBackground, static (s, v) => s with { FootnoteBackground = v }),
            Paint("FootnoteBacklinkForeground", "Footnote back links", inline, static s => s.FootnoteBacklinkForeground, static (s, v) => s with { FootnoteBacklinkForeground = v }),
            Paint("MathForeground", "Formula source", inline, static s => s.MathForeground, static (s, v) => s with { MathForeground = v }),
            Scale("MathScale", "Formula size", inline, static s => s.MathScale, static (s, v) => s with { MathScale = (float)v }, 0.5, 3),

            // Code.
            Font("MonospaceFamily", "Code font", code, static s => s.MonospaceFamily, static (s, v) => s with { MonospaceFamily = v }),
            Choice("CodeTheme", "Code colors", code, [("light", "Light+"), ("dark", "Dark+")],
                static s => s.CodeTheme == HighlightTheme.Dark ? "dark" : "light",
                static (s, v) => v switch { "light" => s with { CodeTheme = HighlightTheme.Light }, "dark" => s with { CodeTheme = HighlightTheme.Dark }, _ => null }),
            Scale("CodeScale", "Inline code size", code, static s => s.CodeScale, static (s, v) => s with { CodeScale = (float)v }, 0.5, 2),
            Paint("CodeBackground", "Inline code background", code, static s => s.CodeBackground, static (s, v) => s with { CodeBackground = v }),
            Paint("CodeBorder", "Inline code border", code, static s => s.CodeBorder, static (s, v) => s with { CodeBorder = v }),
            Optional("CodeForeground", "Inline code text", code, static s => s.CodeForeground, static (s, v) => s with { CodeForeground = v }),
            Padding("CodePadding", "Inline code padding", code, static s => s.CodePadding, static (s, v) => s with { CodePadding = v }, max: 40),
            Paint("CodeBlockBackground", "Code block background", code, static s => s.CodeBlockBackground, static (s, v) => s with { CodeBlockBackground = v }),
            Paint("CodeBlockBorder", "Code block border", code, static s => s.CodeBlockBorder, static (s, v) => s with { CodeBlockBorder = v }),
            Length("CodeBlockCornerRadius", "Code block corner radius", code, static s => s.CodeBlockCornerRadius, static (s, v) => s with { CodeBlockCornerRadius = (float)v }, 0, 30),
            Padding("CodeBlockPadding", "Code block padding", code, static s => s.CodeBlockPadding, static (s, v) => s with { CodeBlockPadding = v }),
            Length("CodeBlockSpacing", "Code block spacing", code, static s => s.CodeBlockSpacing, static (s, v) => s with { CodeBlockSpacing = v }, 0, 100),
            Paint("CodeLanguageForeground", "Code language", code, static s => s.CodeLanguageForeground, static (s, v) => s with { CodeLanguageForeground = v }),
            Paint("CodeLineNumberForeground", "Line numbers", code, static s => s.CodeLineNumberForeground, static (s, v) => s with { CodeLineNumberForeground = v }),
            Length("MathBlockSpacing", "Math block spacing", code, static s => s.MathBlockSpacing, static (s, v) => s with { MathBlockSpacing = v }, 0, 100),
            Paint("FrontMatterBackground", "Front matter background", code, static s => s.FrontMatterBackground, static (s, v) => s with { FrontMatterBackground = v }),
            Padding("FrontMatterPadding", "Front matter padding", code, static s => s.FrontMatterPadding, static (s, v) => s with { FrontMatterPadding = v }),

            // Quotes.
            Paint("QuoteBar", "Quote bar", ThemeGroup.Quotes, static s => s.QuoteBar, static (s, v) => s with { QuoteBar = v }),
            Length("QuoteBarWidth", "Quote bar width", ThemeGroup.Quotes, static s => s.QuoteBarWidth, static (s, v) => s with { QuoteBarWidth = v }, 0, 30),
            Length("QuoteIndent", "Quote indent", ThemeGroup.Quotes, static s => s.QuoteIndent, static (s, v) => s with { QuoteIndent = v }, 0, 100),
            Paint("QuoteForeground", "Quote text", ThemeGroup.Quotes, static s => s.QuoteForeground, static (s, v) => s with { QuoteForeground = v }),

            // Lists.
            Length("ListIndent", "List indent", ThemeGroup.Lists, static s => s.ListIndent, static (s, v) => s with { ListIndent = v }, 0, 100),
            Length("ListItemSpacing", "List item spacing", ThemeGroup.Lists, static s => s.ListItemSpacing, static (s, v) => s with { ListItemSpacing = v }, 0, 100),

            // Tables.
            Paint("TableBorder", "Table border", ThemeGroup.Tables, static s => s.TableBorder, static (s, v) => s with { TableBorder = v }),
            Paint("TableHeaderBackground", "Header background", ThemeGroup.Tables, static s => s.TableHeaderBackground, static (s, v) => s with { TableHeaderBackground = v }),
            Optional("TableCellBackground", "Cell background", ThemeGroup.Tables, static s => s.TableCellBackground, static (s, v) => s with { TableCellBackground = v }),
            Optional("TableStripeBackground", "Striped rows", ThemeGroup.Tables, static s => s.TableStripeBackground, static (s, v) => s with { TableStripeBackground = v }),
            Length("CellPaddingHorizontal", "Cell padding, left and right", ThemeGroup.Tables, static s => s.CellPaddingHorizontal, static (s, v) => s with { CellPaddingHorizontal = v }, 0, 100),
            Length("CellPaddingVertical", "Cell padding, top and bottom", ThemeGroup.Tables, static s => s.CellPaddingVertical, static (s, v) => s with { CellPaddingVertical = v }, 0, 100),

            // Rules and other.
            Paint("Rule", "Horizontal rule", other, static s => s.Rule, static (s, v) => s with { Rule = v }),
            Length("RuleThickness", "Rule thickness", other, static s => s.RuleThickness, static (s, v) => s with { RuleThickness = v }, 0, 20),
            Length("RuleSpacing", "Rule spacing", other, static s => s.RuleSpacing, static (s, v) => s with { RuleSpacing = v }, 0, 100),
            Length("TocSpacing", "Table of contents spacing", other, static s => s.TocSpacing, static (s, v) => s with { TocSpacing = v }, 0, 100),
            Paint("ObjectBackground", "Placeholder background", other, static s => s.ObjectBackground, static (s, v) => s with { ObjectBackground = v }),
            Paint("ObjectBorder", "Placeholder border", other, static s => s.ObjectBorder, static (s, v) => s with { ObjectBorder = v }),
            Paint("PlaceholderForeground", "Placeholder text", other, static s => s.PlaceholderForeground, static (s, v) => s with { PlaceholderForeground = v }),
            Paint("WarningForeground", "Error messages", other, static s => s.WarningForeground, static (s, v) => s with { WarningForeground = v }),
        ]);
        return slots;
    }

    private static StyleSlot Paint(string name, string display, ThemeGroup group, Func<MdStyleSheet, Color> get, Func<MdStyleSheet, Color, MdStyleSheet> set) =>
        new(name, display, group, StyleSlotKind.Color, s => StyleSlot.FormatColor(get(s)), (s, v) => StyleSlot.ParseColor(v) is { } color ? set(s, color) : null);

    private static StyleSlot Optional(string name, string display, ThemeGroup group, Func<MdStyleSheet, Color?> get, Func<MdStyleSheet, Color?, MdStyleSheet> set) =>
        new(name, display, group, StyleSlotKind.OptionalColor, s => get(s) is { } color ? StyleSlot.FormatColor(color) : string.Empty,
            (s, v) => v.Length == 0 ? set(s, null) : StyleSlot.ParseColor(v) is { } color ? set(s, color) : null);

    private static StyleSlot Length(string name, string display, ThemeGroup group, Func<MdStyleSheet, double> get, Func<MdStyleSheet, double, MdStyleSheet> set, double min, double max) =>
        Number(name, display, group, StyleSlotKind.Length, get, set, min, max);

    private static StyleSlot Scale(string name, string display, ThemeGroup group, Func<MdStyleSheet, double> get, Func<MdStyleSheet, double, MdStyleSheet> set, double min, double max) =>
        Number(name, display, group, StyleSlotKind.Scale, get, set, min, max);

    private static StyleSlot Number(string name, string display, ThemeGroup group, StyleSlotKind kind, Func<MdStyleSheet, double> get, Func<MdStyleSheet, double, MdStyleSheet> set, double min, double max) =>
        new(name, display, group, kind, s => StyleSlot.FormatNumber(get(s)), (s, v) => StyleSlot.ParseNumber(v) is { } number && number >= min && number <= max ? set(s, number) : null)
        {
            Minimum = min,
            Maximum = max,
        };

    private static StyleSlot Integer(string name, string display, ThemeGroup group, Func<MdStyleSheet, int> get, Func<MdStyleSheet, int, MdStyleSheet> set, int min, int max) =>
        new(name, display, group, StyleSlotKind.Integer, s => get(s).ToString(System.Globalization.CultureInfo.InvariantCulture),
            (s, v) => int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int number) && number >= min && number <= max ? set(s, number) : null)
        {
            Minimum = min,
            Maximum = max,
        };

    private static StyleSlot Font(string name, string display, ThemeGroup group, Func<MdStyleSheet, string> get, Func<MdStyleSheet, string, MdStyleSheet> set) =>
        new(name, display, group, StyleSlotKind.Font, get, (s, v) => v.Length is > 0 and <= 100 ? set(s, v) : null);

    private static StyleSlot Toggle(string name, string display, ThemeGroup group, Func<MdStyleSheet, bool> get, Func<MdStyleSheet, bool, MdStyleSheet> set) =>
        new(name, display, group, StyleSlotKind.Toggle, s => get(s) ? "true" : "false", (s, v) => bool.TryParse(v, out bool on) ? set(s, on) : null);

    private static StyleSlot Choice(string name, string display, ThemeGroup group, IReadOnlyList<(string Value, string Label)> options, Func<MdStyleSheet, string> get, Func<MdStyleSheet, string, MdStyleSheet?> set) =>
        new(name, display, group, StyleSlotKind.Choice, get, set) { Options = options };

    private static StyleSlot Padding(string name, string display, ThemeGroup group, Func<MdStyleSheet, Microsoft.UI.Xaml.Thickness> get, Func<MdStyleSheet, Microsoft.UI.Xaml.Thickness, MdStyleSheet> set, double max = 500) =>
        new(name, display, group, StyleSlotKind.Padding, s => StyleSlot.FormatPadding(get(s)), (s, v) => StyleSlot.ParsePadding(v, max) is { } padding ? set(s, padding) : null)
        {
            Minimum = 0,
            Maximum = max,
        };
}
