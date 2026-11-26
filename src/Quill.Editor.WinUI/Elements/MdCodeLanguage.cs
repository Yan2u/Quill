using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Localization;
using Quill.Editor.WinUI.Text;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// The language of a code block (Q91): one line of document text in a box outside the code, at its
/// bottom right (Typora). It shares the editor's caret, selection, undo and IME like any text; a
/// placeholder shows while it is empty. Hidden (transparent, still laid out) while the code block is not
/// active.
/// </summary>
public sealed partial class MdCodeLanguage : MdInlineTextView
{
    private bool _isShown;

    public MdCodeLanguage()
    {
        Opacity = 0;
    }

    protected override bool RequiresOwnCanvas => true;

    protected override bool ShouldCreateOwnCanvas => _isShown;

    /// <summary>Whether the box shows: the code block is active.</summary>
    public bool IsShown
    {
        get => _isShown;
        set
        {
            if (_isShown != value)
            {
                _isShown = value;
                Opacity = value ? 1 : 0;
                if (value)
                {
                    InvalidateMeasure();
                    Redraw();
                }
            }
        }
    }

    private protected override BlockTextFormat Format() => base.Format() with { FontSize = Sheet.FontSize * 0.8f, Foreground = Sheet.CodeLanguageForeground };

    protected override Thickness Padding() => new(10, 4, 10, 4);

    protected override void DrawBackground(CanvasDrawingSession session, Size size)
    {
        var box = new Rect(0.5, 0.5, Math.Max(0, size.Width - 1), Math.Max(0, size.Height - 1));
        session.FillRoundedRectangle(box, 3, 3, Sheet.ObjectBackground);
        session.DrawRoundedRectangle(box, 3, 3, Sheet.ObjectBorder);
        if (Content.Runs.IsEmpty)
        {
            using var format = new CanvasTextFormat { FontFamily = Sheet.FontFamily, FontSize = Sheet.FontSize * 0.8f };
            var padding = Padding();
            session.DrawText(UiStrings.Get("editor.codeLanguage"), (float)padding.Left, (float)padding.Top, Sheet.PlaceholderForeground, format);
        }
    }

    protected override void OnNaturalWidthInvalidated() =>
        (ParentOf(this) as MdCodeFence)?.InvalidateLanguageWidth();
}
