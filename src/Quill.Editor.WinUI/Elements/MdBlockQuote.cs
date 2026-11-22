using Microsoft.Graphics.Canvas;
using Microsoft.UI.Xaml;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>A block quote: a bar on the left, its blocks indented and in the quote color.</summary>
public sealed partial class MdBlockQuote : MdContainerView
{
    public MdBlockQuote()
        : base(draws: true)
    {
    }

    protected internal override BlockContext ChildContext => Context with { Foreground = Sheet.QuoteForeground };

    protected override Thickness ContentInset => new(Sheet.QuoteBarWidth + Sheet.QuoteIndent, 0, 0, 0);

    protected override void DrawBlock(CanvasDrawingSession session, Rect region) =>
        session.FillRectangle(new Rect(0, 0, Sheet.QuoteBarWidth, ActualHeight), Sheet.QuoteBar);
}
