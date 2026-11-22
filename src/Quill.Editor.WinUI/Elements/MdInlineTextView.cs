using Quill.Editor.Projection;
using Quill.Editor.WinUI.Text;

namespace Quill.Editor.WinUI.Elements;

/// <summary>A text block whose content is inline Markdown: its <see cref="Content"/> runs (Q3).</summary>
public abstract partial class MdInlineTextView : MdTextBlockView
{
    private InlineContent _content = InlineContent.Empty;

    protected MdInlineTextView(bool draws = true)
        : base(draws)
    {
    }

    public InlineContent Content
    {
        get => _content;
        set
        {
            value ??= InlineContent.Empty;
            if (!Equals(_content, value))
            {
                _content = value;
                InvalidateText();
            }
        }
    }

    private protected override TextSource BuildSource()
    {
        // Images are asked for by this block, so a result for an older version of it is dropped (05).
        Func<string, object?>? images = Context.Images is { } loader && Layout.BlockGeometry.ParseKey(BlockId) is { } id
            ? source => loader.Get(source, id)
            : null;
        return InlineContentFormatter.Format(_content, Sheet, images);
    }

    private protected override BlockTextFormat Format() => new(Sheet.FontFamily, Sheet.FontSize, Context.Foreground)
    {
        // A paragraph holding only an image shows it centered (10, Typora).
        Alignment = _content.Runs is [{ Role: RunRole.Image }] ? Microsoft.Graphics.Canvas.Text.CanvasHorizontalAlignment.Center : default,
    };
}
