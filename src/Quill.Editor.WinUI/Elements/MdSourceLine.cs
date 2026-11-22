using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Rendering;
using Quill.Editor.WinUI.Text;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A line of the source mode (11-source-mode.md): monospace at the body size, no padding, colored by
/// the document's <see cref="SourceHighlighting"/> (Light+ or Dark+). Until its line is analyzed, or in
/// high contrast, the text shows in the plain foreground.
/// </summary>
public sealed partial class MdSourceLine : MdRawTextView
{
    private protected override TextSource BuildSource()
    {
        string text = Text;
        if (Context.Highlighting?.SpansOf(BlockId) is not { } spans || spans.Sum(static span => span.Length) != text.Length)
        {
            return TextSource.Plain(text);
        }

        uint plain = Context.Highlighting.DefaultForeground;
        var builder = new TextSource.Builder();
        foreach (var span in spans)
        {
            var style = HighlightStyles.StyleOf(span, plain);
            builder.Append(text.Substring(span.Start, span.Length), style);
        }

        return builder.Build();
    }

    private protected override BlockTextFormat Format() => new(Sheet.FontFamily, Sheet.FontSize, Context.Foreground, Monospace: true);

    protected override Thickness Padding() => default;
}
