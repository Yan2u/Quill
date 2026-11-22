using Quill.Editor.Projection;
using Quill.Editor.WinUI.Theming;

namespace Quill.Editor.WinUI.Text;

/// <summary>
/// Turns a text block's <see cref="InlineContent"/> into a <see cref="TextSource"/>: every visual
/// decision about inline elements (10-elements.md) is made here, from the style sheet. Each run
/// contributes exactly its display length, so display offsets stay layout offsets.
/// </summary>
internal static class InlineContentFormatter
{
    /// <param name="images">The decoded image for a source, or null while it loads; null shows placeholders.</param>
    public static TextSource Format(InlineContent content, MdStyleSheet sheet, Func<string, object?>? images = null)
    {
        if (content.Runs.IsDefaultOrEmpty)
        {
            return TextSource.Empty;
        }

        var builder = new TextSource.Builder();
        foreach (var run in content.Runs)
        {
            var style = StyleOf(run, sheet);
            int length = run.Display.Length;
            switch (run.Role)
            {
                case RunRole.Text or RunRole.Marker or RunRole.LineBreak:
                    builder.Append(Fit(run.Text, length), style);
                    break;
                case RunRole.Entity when run.Text.Length == 1 && !char.IsSurrogate(run.Text[0]):
                    builder.Append(run.Text, style);
                    break;
                case RunRole.Math:
                    builder.AppendObject(TextObjectKind.Math, run.Value ?? run.Text, style);
                    break;
                case RunRole.Image:
                    builder.AppendObject(TextObjectKind.Image, run.Text, style, images?.Invoke(run.Value ?? string.Empty));
                    break;
                case RunRole.Emoji:
                    builder.AppendObject(TextObjectKind.Emoji, run.Text, style);
                    break;
                default:
                    builder.AppendObject(TextObjectKind.Entity, run.Text, style);
                    break;
            }
        }

        return content.IsImageOnly ? new TextSource(builder.Build()) : builder.Build();
    }

    private static SpanStyle StyleOf(InlineRun run, MdStyleSheet sheet)
    {
        var flags = run.Style;
        bool code = flags.HasFlag(InlineStyle.Code);
        bool keyboard = flags.HasFlag(InlineStyle.Keyboard);
        bool footnote = flags.HasFlag(InlineStyle.FootnoteReference);
        bool marker = run.Role == RunRole.Marker;
        var background = code ? SpanBackground.Code
            : keyboard ? SpanBackground.Keyboard
            : flags.HasFlag(InlineStyle.Highlight) ? SpanBackground.Highlight
            : footnote ? SpanBackground.Footnote
            : SpanBackground.None;

        // A footnote reference stays a small superscript while active (10).
        var script = flags.HasFlag(InlineStyle.Superscript) || footnote ? ScriptShift.Superscript
            : flags.HasFlag(InlineStyle.Subscript) ? ScriptShift.Subscript
            : ScriptShift.None;
        var foreground = marker || flags.HasFlag(InlineStyle.Html) ? sheet.SecondaryForeground
            : flags.HasFlag(InlineStyle.Math) ? sheet.MathForeground
            : footnote ? sheet.FootnoteForeground
            : keyboard ? sheet.KeyboardForeground
            : flags.HasFlag(InlineStyle.Highlight) && sheet.HighlightForeground is { } highlighted ? highlighted
            : code && sheet.CodeForeground is { } codeColor ? codeColor
            : flags.HasFlag(InlineStyle.Link) ? sheet.LinkForeground
            : (Windows.UI.Color?)null;

        // Markers of active elements are monospace and secondary (10).
        bool monospace = code || keyboard || marker || flags.HasFlag(InlineStyle.Math);
        return new SpanStyle(
            Bold: flags.HasFlag(InlineStyle.Strong) || (footnote && sheet.FootnoteBold),
            Italic: flags.HasFlag(InlineStyle.Emphasis),
            Monospace: monospace,
            Strikethrough: flags.HasFlag(InlineStyle.Strikethrough) && !marker,
            Underline: (flags.HasFlag(InlineStyle.Underline) || (sheet.LinkUnderline && flags.HasFlag(InlineStyle.Link) && !footnote)) && !marker,
            Foreground: foreground,
            Background: background,
            Script: script,
            Scale: code || keyboard ? sheet.CodeScale : 1);
    }

    /// <summary>Keeps a run exactly as long as its display range, whatever its text.</summary>
    private static string Fit(string text, int length) =>
        text.Length == length ? text : text.Length > length ? text[..length] : text.PadRight(length, '​');
}
