using Microsoft.Graphics.Canvas;
using Quill.Editor.WinUI.Theming;
using Windows.Foundation;
using Windows.UI;

namespace Quill.Editor.WinUI.Elements;

/// <summary>The box behind code and math blocks: a rounded fill and, when the style sheet has one, a frame.</summary>
internal static class CodeBox
{
    public static void Draw(CanvasDrawingSession session, Rect box, Color background, MdStyleSheet sheet)
    {
        float radius = sheet.CodeBlockCornerRadius;
        session.FillRoundedRectangle(box, radius, radius, background);
        if (sheet.CodeBlockBorder.A > 0 && box.Width > 1 && box.Height > 1)
        {
            var frame = new Rect(box.X + 0.5, box.Y + 0.5, box.Width - 1, box.Height - 1);
            session.DrawRoundedRectangle(frame, radius, radius, sheet.CodeBlockBorder, 1);
        }
    }
}
