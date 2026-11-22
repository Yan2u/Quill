using System.Collections.Concurrent;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace Quill.Editor.WinUI.Theming;

/// <summary>
/// The style slots of the editor (05-projection-and-view.md): every visual value the element
/// controls draw with. A theme is one instance; switching themes swaps the instance and redraws,
/// it never re-projects. The projection (VNode) carries no style values at all.
/// Defaults implement the MIT-licensed Light theme based on <c>sindresorhus/github-markdown-css</c>
/// (<c>github-markdown-light.css</c>) and <c>primer/primitives</c> (<c>light</c>), while preserving
/// Quill's font families and page geometry (see <c>MdStyleSheet.Themes.cs</c>).
/// </summary>
public sealed partial record MdStyleSheet
{
    private static readonly ConcurrentDictionary<string, float> NaturalLineHeights = new();

    // Page (05: page-level slots).
    public Color PageBackground { get; init; } = Rgb(0xFFFFFF);

    public Color Foreground { get; init; } = Rgb(0x1F2328);

    /// <summary>Markers of active elements, decorations of definitions, heading badges.</summary>
    public Color SecondaryForeground { get; init; } = Rgb(0x818B98);

    public string FontFamily { get; init; } = "Segoe UI Variable Display";

    public string MonospaceFamily { get; init; } = "Cascadia Mono";

    public float FontSize { get; init; } = 16;

    /// <summary>The height of a line as a multiple of the font size, as CSS <c>line-height</c>.</summary>
    public float LineHeight { get; init; } = 1.5f;

    public double ContentMaxWidth { get; init; } = 800;

    public double PagePaddingHorizontal { get; init; } = 30;

    public double PagePaddingVertical { get; init; } = 30;

    public float IncrementalTabStop { get; init; } = 32;

    // Spacing (10: lists use a smaller spacing than paragraphs). The margins of block kinds; adjacent
    // margins collapse to the larger (Layout.BlockMargins), as in CSS.
    public double BlockSpacing { get; init; } = 16;

    public double HeadingSpacing { get; init; } = 20;

    public double CodeBlockSpacing { get; init; } = 16;

    public double MathBlockSpacing { get; init; } = 16;

    public double RuleSpacing { get; init; } = 24;

    public double TocSpacing { get; init; } = 16;

    public double ListItemSpacing { get; init; } = 4;

    // Inline elements.
    public Color LinkForeground { get; init; } = Rgb(0x0969DA);

    public bool LinkUnderline { get; init; }

    public Color SelectionBackground { get; init; } = Rgba(0x0969DA, 0x33);

    /// <summary>Selected text; null keeps its colors (Dark Dimmed uses white).</summary>
    public Color? SelectionForeground { get; init; }

    /// <summary>Behind every match of the find panel (12-find-replace.md).</summary>
    public Color SearchMatchBackground { get; init; } = Rgba(0xEAC54F, 0x66);

    /// <summary>Behind the current match (the one the selection is).</summary>
    public Color SearchCurrentBackground { get; init; } = Rgba(0xFB8F44, 0xB3);

    public Color CompositionUnderline { get; init; } = Rgb(0x1F2328);

    public Color HighlightBackground { get; init; } = Rgb(0xFFF8C5);

    /// <summary>Highlighted text (<c>==mark==</c>); null keeps the text color.</summary>
    public Color? HighlightForeground { get; init; } = Rgb(0x1F2328);

    public Color CodeBackground { get; init; } = Rgba(0x818B98, 0x1F);

    /// <summary>The frame of inline code; transparent for none.</summary>
    public Color CodeBorder { get; init; } = Colors.Transparent;

    /// <summary>Inline code text color; null keeps the surrounding block's color.</summary>
    public Color? CodeForeground { get; init; }

    /// <summary>Padding around inline code glyphs inside their background box.</summary>
    public Thickness CodePadding { get; init; } = new(2, 2, 2, 2);

    /// <summary>The corner radius of the boxes of inline code and keyboard text.</summary>
    public float InlineCornerRadius { get; init; } = 6;

    /// <summary>Inline code and keyboard text relative to the surrounding text.</summary>
    public float CodeScale { get; init; } = 0.85f;

    /// <summary>Superscript, subscript and footnote references relative to the surrounding text.</summary>
    public float ScriptScale { get; init; } = 0.75f;

    public Color KeyboardForeground { get; init; } = Rgb(0x1F2328);

    public Color KeyboardBackground { get; init; } = Rgb(0xF6F8FA);

    public Color KeyboardBorder { get; init; } = Rgba(0xD1D9E0, 0xB3);

    public Color FootnoteForeground { get; init; } = Rgb(0x0969DA);

    /// <summary>Whether footnote references are bold.</summary>
    public bool FootnoteBold { get; init; }

    public Color FootnoteBackground { get; init; } = Rgba(0x0969DA, 0x14);

    /// <summary>The ↩ backlinks after a footnote definition (10: one per reference).</summary>
    public Color FootnoteBacklinkForeground { get; init; } = Rgb(0x0969DA);

    /// <summary>Math source shown as a placeholder (formulas that do not parse); typeset formulas use the text color.</summary>
    public Color MathForeground { get; init; } = Rgb(0x8250DF);

    /// <summary>Typeset formulas relative to the surrounding text (M6.3, default; KaTeX uses 1.21).</summary>
    public float MathScale { get; init; } = 1.1f;

    /// <summary>Placeholders of images and other objects that are not rendered yet (M5.5).</summary>
    public Color ObjectBackground { get; init; } = Rgb(0xF6F8FA);

    public Color ObjectBorder { get; init; } = Rgb(0xD1D9E0);

    public Color PlaceholderForeground { get; init; } = Rgb(0x59636E);

    /// <summary>Messages about content that does not render (a Mermaid source with an error).</summary>
    public Color WarningForeground { get; init; } = Rgb(0xD1242F);

    // Blocks.
    public Color HeadingForeground { get; init; } = Rgb(0x1F2328);

    /// <summary>The weight of headings and table header cells (CSS <c>font-weight: 600</c>).</summary>
    public ushort BlockBoldWeight { get; init; } = 600;

    /// <summary>Level 6 headings (Primer <c>fgColor.muted</c>).</summary>
    public Color Heading6Foreground { get; init; } = Rgb(0x59636E);

    /// <summary>Heading sizes, level 1 to 6, relative to <see cref="FontSize"/>.</summary>
    public IReadOnlyList<float> HeadingScales { get; init; } = [2.0f, 1.5f, 1.25f, 1.0f, 0.875f, 0.85f];

    /// <summary>The line height of headings, as <see cref="LineHeight"/>.</summary>
    public float HeadingLineHeight { get; init; } = 1.25f;

    /// <summary>The rule under headings of level 1 to <see cref="HeadingRuleLevels"/> (h1 and h2 in github-markdown-css).</summary>
    public Color HeadingRule { get; init; } = Rgba(0xD1D9E0, 0xB3);

    public double HeadingRuleThickness { get; init; } = 1;

    public int HeadingRuleLevels { get; init; } = 2;

    /// <summary>The space between a heading's text and its rule.</summary>
    public double HeadingRuleGap { get; init; } = 6;

    public Color CodeBlockBackground { get; init; } = Rgb(0xF6F8FA);

    /// <summary>The frame of code and math blocks; transparent for none.</summary>
    public Color CodeBlockBorder { get; init; } = Colors.Transparent;

    public float CodeBlockCornerRadius { get; init; } = 6;

    public Thickness CodeBlockPadding { get; init; } = new(12, 6, 12, 6);

    /// <summary>The code block's language indicator foreground.</summary>
    public Color CodeLanguageForeground { get; init; } = Rgb(0x59636E);

    /// <summary>The line numbers in a code block's gutter (14: "show line numbers in code blocks")〔默认〕.</summary>
    public Color CodeLineNumberForeground { get; init; } = Rgb(0x818B98);

    /// <summary>Front matter block background.</summary>
    public Color FrontMatterBackground { get; init; } = Rgb(0xF6F8FA);

    public Thickness FrontMatterPadding { get; init; } = new(16);

    /// <summary>The colors of code blocks (M6.3, Q87: Light+ or Dark+); null draws code in the plain foreground (high contrast).</summary>
    public Quill.Highlighting.HighlightTheme? CodeTheme { get; init; } = Quill.Highlighting.HighlightTheme.Light;

    public Color QuoteBar { get; init; } = Rgb(0xD1D9E0);

    public double QuoteBarWidth { get; init; } = 4;

    public double QuoteIndent { get; init; } = 16;

    public Color QuoteForeground { get; init; } = Rgb(0x59636E);

    public double ListIndent { get; init; } = 32;

    public Color TableBorder { get; init; } = Rgb(0xD1D9E0);

    public Color TableHeaderBackground { get; init; } = Rgb(0xFFFFFF);

    /// <summary>The background of body cells; null for the surface around the table.</summary>
    public Color? TableCellBackground { get; init; }

    /// <summary>The background of every second body row (<c>tr:nth-child(2n)</c>); null for no stripes.</summary>
    public Color? TableStripeBackground { get; init; } = Rgb(0xF6F8FA);

    public double CellPaddingHorizontal { get; init; } = 13;

    public double CellPaddingVertical { get; init; } = 6;

    public Color Rule { get; init; } = Rgb(0xD1D9E0);

    public double RuleThickness { get; init; } = 4;

    /// <summary>The frame of a block caret (Q59: a dashed frame around a TOC, a border around a rule).</summary>
    public Color BlockCaret { get; init; } = Rgb(0x0969DA);

    /// <summary>Drawn over a block selected whole.</summary>
    public Color BlockSelection { get; init; } = Rgba(0x0969DA, 0x33);

    /// <summary>The factor Win2D's proportional line spacing needs for <see cref="LineHeight"/> in the body font.</summary>
    public float LineSpacing => LineSpacingFor(FontFamily, LineHeight);

    public float HeadingSize(int level) => FontSize * HeadingScales[Math.Clamp(level, 1, HeadingScales.Count) - 1];

    /// <summary>The proportional line spacing that gives lines of <paramref name="lineHeight"/> font sizes in <paramref name="family"/>.</summary>
    public static float LineSpacingFor(string family, float lineHeight) => lineHeight / NaturalLineHeight(family);

    /// <summary>The natural line height of a font as a multiple of its size, measured once per family.</summary>
    private static float NaturalLineHeight(string family) => NaturalLineHeights.GetOrAdd(family, static name =>
    {
        const float size = 100;
        using var format = new CanvasTextFormat { FontFamily = name, FontSize = size };
        using var layout = new CanvasTextLayout(CanvasDevice.GetSharedDevice(), "x", format, 1000, 1000);
        return layout.LineMetrics.Length > 0 && layout.LineMetrics[0].Height > 0 ? layout.LineMetrics[0].Height / size : 1.33f;
    });

    private static Color Rgb(uint value) => Color.FromArgb(0xFF, (byte)(value >> 16), (byte)(value >> 8), (byte)value);

    private static Color Rgba(uint value, byte alpha) => Color.FromArgb(alpha, (byte)(value >> 16), (byte)(value >> 8), (byte)value);
}
