using Quill.Editor.WinUI.Text;
using Quill.Highlighting;
using Windows.UI;

namespace Quill.Editor.WinUI.Rendering;

/// <summary>Colored runs of a highlighter as the spans of a text source.</summary>
internal static class HighlightStyles
{
    /// <summary>
    /// The style of a run; a run in the theme's plain color takes no color, so it draws in the style
    /// sheet's foreground (the default style when it has no font style either).
    /// </summary>
    public static SpanStyle StyleOf(HighlightSpan span, uint plain) => new(
        Bold: span.Style.HasFlag(HighlightFontStyle.Bold),
        Italic: span.Style.HasFlag(HighlightFontStyle.Italic),
        Strikethrough: span.Style.HasFlag(HighlightFontStyle.Strikethrough),
        Underline: span.Style.HasFlag(HighlightFontStyle.Underline),
        Foreground: span.Foreground == plain ? null : Color.FromArgb((byte)(span.Foreground >> 24), (byte)(span.Foreground >> 16), (byte)(span.Foreground >> 8), (byte)span.Foreground));
}
