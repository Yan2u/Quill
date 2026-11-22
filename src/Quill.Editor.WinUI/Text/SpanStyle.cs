using Windows.UI;

namespace Quill.Editor.WinUI.Text;

/// <summary>The background a span draws behind its text.</summary>
internal enum SpanBackground
{
    None,
    Code,
    Highlight,
    Keyboard,
    Footnote,
}

/// <summary>Raises or lowers a span off the baseline, at a smaller size.</summary>
internal enum ScriptShift
{
    None,
    Superscript,
    Subscript,
}

/// <summary>The formatting of one span of a block's text; every value comes from the style sheet.</summary>
internal readonly record struct SpanStyle(
    bool Bold = false,
    bool Italic = false,
    bool Monospace = false,
    bool Strikethrough = false,
    bool Underline = false,
    Color? Foreground = null,
    SpanBackground Background = SpanBackground.None,
    ScriptShift Script = ScriptShift.None,
    float Scale = 1);

/// <summary>The base format of a block's text.</summary>
/// <param name="Alignment">Horizontal alignment: table cells and centered images.</param>
/// <param name="LineHeight">The line height as a multiple of the font size; 0 for the style sheet's.</param>
internal readonly record struct BlockTextFormat(
    string FontFamily,
    float FontSize,
    Color Foreground,
    bool Bold = false,
    bool Monospace = false,
    Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment Alignment = Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment.Left,
    bool Wrap = true,
    float LineHeight = 0);
