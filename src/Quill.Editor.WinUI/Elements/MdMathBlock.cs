using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.UI.Xaml;
using Quill.Editor.WinUI.Rendering.Formulas;
using Quill.Editor.WinUI.Text;
using RaTeX;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Elements;

/// <summary>
/// A display math block (10). Not focused, it is one object: the formula typeset by RaTeX (M6.3),
/// centered; a formula that does not parse shows its TeX source centered in the math color instead.
/// Focused, it shows its source like code with a live preview of the formula below it, the full column
/// wide (as Typora); while the source does not parse, the last formula that did stays in the preview.
/// A formula wider than the column scrolls horizontally with a scroll bar below it. The source layout is
/// kept either way, so the layout service can always query it.
/// </summary>
public sealed partial class MdMathBlock : MdRawTextView
{
    private DisplayList? _lastPreview;

    /// <summary>The typeset formula shown while not focused, or null (focused, empty, or not parsed).</summary>
    internal DisplayList? Formula => IsFocused ? null : MathTypesetter.Typeset(Text, display: true);

    /// <summary>The live preview below the source while focused, or null.</summary>
    internal DisplayList? Preview => IsFocused ? (_lastPreview = MathTypesetter.Typeset(Text, display: true) ?? _lastPreview) : null;

    /// <summary>The formula drawn: alone while not focused, as the preview while focused.</summary>
    private DisplayList? Shown => IsFocused ? Preview : Formula;

    private float FormulaSize => Sheet.FontSize * Sheet.MathScale;

    private protected override BlockTextFormat Format() => IsFocused
        ? base.Format()
        : base.Format() with { Foreground = Sheet.MathForeground, Alignment = CanvasHorizontalAlignment.Center };

    protected override Thickness Padding() => Sheet.CodeBlockPadding;

    private protected override bool ManagesOverflow => true;

    protected override void OnFocusChanged()
    {
        _lastPreview = null;
        InvalidateText();
    }

    protected override void OnBlockUnmounted()
    {
        _lastPreview = null;
        base.OnBlockUnmounted();
    }

    protected override Size MeasureBlock(Size availableSize)
    {
        var size = base.MeasureBlock(availableSize);
        if (Shown is not { } formula)
        {
            SetOverflow(0, 1);
            return size;
        }

        var padding = Padding();
        double formulaWidth = formula.Width * FormulaSize;
        double width = double.IsInfinity(availableSize.Width) ? formulaWidth + padding.Left + padding.Right : size.Width;
        double bar = SetOverflow(formulaWidth, Math.Max(1, width - padding.Left - padding.Right));
        double height = formula.TotalHeight * FormulaSize + padding.Top + padding.Bottom + bar;
        return new Size(width, IsFocused ? size.Height + height : height);
    }

    protected override void DrawBlock(CanvasDrawingSession session, Rect region)
    {
        if (IsFocused || Formula is null)
        {
            base.DrawBlock(session, region);
        }

        if (Shown is not { } formula)
        {
            return;
        }

        // Centered; wider than the column, it starts at the left padding and scrolls.
        var padding = Padding();
        double column = Math.Max(1, ActualWidth - padding.Left - padding.Right);
        float width = (float)formula.Width * FormulaSize;
        float left = IsOverflowing ? (float)(padding.Left - HorizontalOffset) : (float)(padding.Left + (column - width) / 2);
        float top = (float)((IsFocused ? SourceHeight() : 0) + padding.Top);
        using (session.CreateLayer(1, new Rect(padding.Left, 0, column, ActualHeight)))
        {
            MathPainter.Draw(session, formula, new Vector2(left, top), FormulaSize, Context.Foreground, Sheet.FontFamily);
        }

        // Selected while shown as a formula (05: rendered objects have their own selected look).
        if (!IsFocused && (IsSelected || SelectionEnd > SelectionStart))
        {
            session.FillRectangle(new Rect(Math.Max(padding.Left, left), top, Math.Min(width, column), formula.TotalHeight * FormulaSize), Sheet.BlockSelection);
        }
    }

    protected override void DrawBackground(CanvasDrawingSession session, Size size)
    {
        if (IsFocused)
        {
            CodeBox.Draw(session, new Rect(0, 0, size.Width, SourceHeight()), Sheet.CodeBlockBackground, Sheet);
        }
    }

    /// <summary>The height of the source part while focused: the text and its padding.</summary>
    private double SourceHeight()
    {
        var padding = Padding();
        return (TextLayout?.Height ?? 0) + padding.Top + padding.Bottom;
    }
}
