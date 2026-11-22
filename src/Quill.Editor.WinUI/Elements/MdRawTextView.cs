using Microsoft.Graphics.Canvas;
using Quill.Editor.WinUI.Text;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A block whose content is raw text without inline syntax (02: raw-text leaves): its displayed
/// text is its source, in the monospace font.
/// </summary>
public abstract partial class MdRawTextView : MdTextBlockView
{
    private string _text = string.Empty;

    public string Text
    {
        get => _text;
        set
        {
            value ??= string.Empty;
            if (_text != value)
            {
                _text = value;
                InvalidateText();
            }
        }
    }

    private protected override TextSource BuildSource() => TextSource.Plain(_text);

    private protected override ITextBlockLayout CreateLayout(TextSource source, BlockTextFormat format, float width, PiecewiseTextLayout? previous) =>
        PiecewiseTextLayout.Suits(_text)
            ? PiecewiseTextLayout.Create(CanvasDevice.GetSharedDevice(), source, format, Sheet, width, previous)
            : base.CreateLayout(source, format, width, previous);

    private protected override BlockTextFormat Format() => new(Sheet.FontFamily, Sheet.FontSize * Sheet.CodeScale, Context.Foreground, Monospace: true);
}
