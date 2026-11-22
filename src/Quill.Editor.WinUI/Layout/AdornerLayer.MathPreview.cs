using System.Numerics;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.Editor.Projection;
using Quill.Editor.WinUI.Elements;
using Quill.Editor.WinUI.Rendering.Formulas;
using Quill.Editor.WinUI.Theming;
using RaTeX;
using Windows.Foundation;

namespace Quill.Editor.WinUI.Layout;

/// <summary>
/// The preview of active inline math (10): the source shows in the text, the typeset formula in a
/// flyout below it. The projection declares the preview (<see cref="InlinePreview"/>); this only draws
/// it. While the source does not parse, the last formula that did stays.
/// </summary>
internal sealed partial class AdornerLayer
{
    private const double PreviewPadding = 6;

    private Border? _mathHost;
    private CanvasControl? _mathCanvas;
    private DisplayList? _mathFormula;
    private string? _mathSource;
    private string? _mathBlock;
    private float _mathSize;
    private Windows.UI.Color _mathColor;

    /// <summary>The inline math preview as shown, for the probes: its TeX source and bounds in page coordinates.</summary>
    public (string Source, Rect Bounds)? MathPreview { get; private set; }

    private void UpdateMathPreview(BlockGeometry geometry, IEnumerable<MdBlockView> realized, MdStyleSheet sheet, Point origin)
    {
        var view = realized.OfType<MdInlineTextView>().FirstOrDefault(static view => view.Content.Previews.Any(static preview => preview.Role == RunRole.Math));
        var preview = view?.Content.Previews.First(static preview => preview.Role == RunRole.Math);
        if (view?.TextLayout is not { } layout || preview is null)
        {
            HideMathPreview();
            return;
        }

        // A new formula only when the source changed; the last one that parsed stays otherwise.
        if (_mathSource != preview.Value || _mathBlock != view.BlockId)
        {
            _mathFormula = MathTypesetter.Typeset(preview.Value, display: false) ?? (_mathBlock == view.BlockId ? _mathFormula : null);
            _mathSource = preview.Value;
            _mathBlock = view.BlockId;
            _mathCanvas?.Invalidate();
        }

        var regions = layout.Regions(preview.Display.Start, preview.Display.End);
        if (_mathFormula is not { } formula || regions.Count == 0)
        {
            HideMathPreview();
            return;
        }

        var host = EnsureMathHost(sheet);
        float size = sheet.FontSize * sheet.MathScale;
        double width = Math.Ceiling(formula.Width * size) + 2;
        double height = Math.Ceiling(formula.TotalHeight * size) + 2;
        if (Math.Abs(_mathCanvas!.Width - width) > 0.01 || Math.Abs(_mathCanvas.Height - height) > 0.01 || _mathSize != size || _mathColor != sheet.Foreground)
        {
            _mathCanvas.Width = width;
            _mathCanvas.Height = height;
            _mathSize = size;
            _mathColor = sheet.Foreground;
            _mathCanvas.Invalidate();
        }

        var text = geometry.TextOrigin(view);
        double left = text.X + regions.Min(static region => region.X);
        double bottom = text.Y + regions.Max(static region => region.Bottom);
        Place(host, left - origin.X, bottom + HoverGap - origin.Y);
        MathPreview = (preview.Value, new Rect(left, bottom + HoverGap, width + 2 * PreviewPadding, height + 2 * PreviewPadding));
    }

    private Border EnsureMathHost(MdStyleSheet sheet)
    {
        if (_mathHost is null)
        {
            _mathCanvas = new CanvasControl { IsHitTestVisible = false };
            _mathCanvas.Draw += OnMathCanvasDraw;
            _mathHost = new Border
            {
                Child = _mathCanvas,
                Padding = new Thickness(PreviewPadding),
                CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
            };
            Children.Add(_mathHost);
        }

        if (_mathCanvas!.ClearColor != sheet.PageBackground)
        {
            _mathCanvas.ClearColor = sheet.PageBackground;
            _mathHost.Background = new SolidColorBrush(sheet.PageBackground);
            _mathHost.BorderBrush = new SolidColorBrush(sheet.ObjectBorder);
        }

        return _mathHost;
    }

    private void OnMathCanvasDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        if (_mathFormula is { } formula)
        {
            MathPainter.Draw(args.DrawingSession, formula, new Vector2(1, 1), _mathSize, _mathColor, _sheet?.FontFamily ?? MdStyleSheet.Light.FontFamily);
        }
    }

    private void ReleaseMathPreview()
    {
        MathPreview = null;
        if (_mathCanvas is { } canvas)
        {
            canvas.Draw -= OnMathCanvasDraw;
            canvas.RemoveFromVisualTree();
            if (_mathHost is not null)
            {
                _mathHost.Child = null;
            }

            _mathCanvas = null;
        }

        if (_mathHost is not null)
        {
            Children.Remove(_mathHost);
            _mathHost = null;
        }

        _mathBlock = null;
        _mathSource = null;
        _mathFormula = null;
    }

    private void HideMathPreview()
    {
        MathPreview = null;
        if (_mathHost is not null)
        {
            Show(_mathHost, false);
        }

        if (_mathBlock is not null)
        {
            _mathBlock = null;
            _mathSource = null;
            _mathFormula = null;
        }
    }
}
