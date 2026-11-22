using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Quill.Editor.WinUI.Theming;

/// <summary>
/// The default themes. Light and Dark follow MIT-licensed <c>sindresorhus/github-markdown-css</c>
/// (<c>github-markdown-light.css</c> and <c>github-markdown-dark-dimmed.css</c>) and
/// <c>primer/primitives</c> (<c>light</c> and <c>dark-dimmed</c>), while keeping Quill's font
/// families (<c>Segoe UI Variable Display</c>, <c>Cascadia Mono</c>) and page geometry.
/// </summary>
public sealed partial record MdStyleSheet
{
    public static MdStyleSheet Light { get; } = new();

    public static MdStyleSheet Dark { get; } = new()
    {
        CodeTheme = Quill.Highlighting.HighlightTheme.Dark,
        PageBackground = Rgb(0x212830),
        Foreground = Rgb(0xD1D7E0),
        SecondaryForeground = Rgb(0x656C76),
        LinkForeground = Rgb(0x478BE6),
        SelectionBackground = Rgba(0x316DCA, 0xB3),
        SelectionForeground = Rgb(0xFFFFFF),
        SearchMatchBackground = Rgba(0xAE7C14, 0x59),
        SearchCurrentBackground = Rgb(0xC69026),
        CompositionUnderline = Rgb(0xD1D7E0),
        HighlightBackground = Rgba(0xAE7C14, 0x40),
        HighlightForeground = Rgb(0xF0F6FC),
        CodeBackground = Rgba(0x656C76, 0x33),
        KeyboardForeground = Rgb(0xF0F6FC),
        KeyboardBackground = Rgb(0x262C36),
        KeyboardBorder = Rgba(0x3D444D, 0xB3),
        FootnoteForeground = Rgb(0x478BE6),
        FootnoteBackground = Rgba(0x4184E4, 0x1A),
        FootnoteBacklinkForeground = Rgb(0x478BE6),
        MathForeground = Rgb(0x986EE2),
        ObjectBackground = Rgb(0x262C36),
        ObjectBorder = Rgb(0x3D444D),
        PlaceholderForeground = Rgb(0x9198A1),
        WarningForeground = Rgb(0xE5534B),
        HeadingForeground = Rgb(0xD1D7E0),
        Heading6Foreground = Rgb(0x9198A1),
        HeadingRule = Rgba(0x3D444D, 0xB3),
        CodeBlockBackground = Rgb(0x262C36),
        CodeLanguageForeground = Rgb(0x9198A1),
        CodeLineNumberForeground = Rgb(0x656C76),
        FrontMatterBackground = Rgb(0x262C36),
        QuoteBar = Rgb(0x3D444D),
        QuoteForeground = Rgb(0x9198A1),
        TableBorder = Rgb(0x3D444D),
        TableHeaderBackground = Rgb(0x212830),
        TableStripeBackground = Rgb(0x262C36),
        Rule = Rgb(0x3D444D),
        BlockCaret = Rgb(0x316DCA),
        BlockSelection = Rgba(0x316DCA, 0x66),
    };

    /// <summary>The system colors of the active high-contrast theme.</summary>
    public static MdStyleSheet HighContrast()
    {
        var settings = new UISettings();
        Color window = settings.UIElementColor(UIElementType.Window);
        Color text = settings.UIElementColor(UIElementType.WindowText);
        Color highlight = settings.UIElementColor(UIElementType.Highlight);
        Color link = settings.UIElementColor(UIElementType.Hotlight);
        Color gray = settings.UIElementColor(UIElementType.GrayText);
        Color highlightText = settings.UIElementColor(UIElementType.HighlightText);
        return Light with
        {
            CodeTheme = null,
            PageBackground = window,
            Foreground = text,
            SecondaryForeground = gray,
            HeadingForeground = text,
            Heading6Foreground = text,
            HeadingRule = text,
            HeadingRuleThickness = 1,
            LinkForeground = link,
            SelectionBackground = highlight,
            SelectionForeground = highlightText,
            SearchMatchBackground = Color.FromArgb(0x60, highlight.R, highlight.G, highlight.B),
            SearchCurrentBackground = highlight,
            CompositionUnderline = text,
            HighlightBackground = window,
            HighlightForeground = text,
            CodeBackground = window,
            CodeBorder = text,
            CodeBlockBackground = window,
            CodeBlockBorder = text,
            CodeLanguageForeground = text,
            CodeLineNumberForeground = gray,
            FrontMatterBackground = window,
            KeyboardForeground = text,
            KeyboardBackground = window,
            KeyboardBorder = text,
            FootnoteForeground = link,
            FootnoteBackground = window,
            FootnoteBacklinkForeground = link,
            MathForeground = text,
            ObjectBackground = window,
            ObjectBorder = text,
            PlaceholderForeground = gray,
            WarningForeground = text,
            QuoteBar = text,
            QuoteForeground = text,
            TableBorder = text,
            TableHeaderBackground = window,
            TableCellBackground = null,
            TableStripeBackground = null,
            Rule = text,
            BlockCaret = highlight,
            BlockSelection = Color.FromArgb(0x80, highlight.R, highlight.G, highlight.B),
        };
    }
}
