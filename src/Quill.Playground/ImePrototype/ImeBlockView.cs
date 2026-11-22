using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Text;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Playground.TextPrototype;
using Windows.Foundation;
using Windows.UI;

namespace Quill.Playground.ImePrototype;

/// <summary>
/// A plain text block of the IME lab, drawn like the real block controls will be (Q65): on a
/// <see cref="CanvasVirtualControl"/>, opaque with the page background, text with ClearType.
/// Draws the selection, the composition underline and a thick underline on the target clause.
/// </summary>
internal sealed partial class ImeBlockView : Grid
{
    private readonly CanvasVirtualControl _canvas;
    private string _text = string.Empty;
    private StyledTextLayout? _layout;
    private float _layoutWidth = -1;

    public ImeBlockView(Color pageBackground)
    {
        PageBackground = pageBackground;
        _canvas = new CanvasVirtualControl { ClearColor = pageBackground };
        _canvas.RegionsInvalidated += OnRegionsInvalidated;
        Children.Add(_canvas);
        Unloaded += (_, _) =>
        {
            _layout?.Dispose();
            _layout = null;
            _layoutWidth = -1;
        };
    }

    public Color PageBackground { get; }

    public TextTheme Theme { get; set; } = TextTheme.Light;

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            _layoutWidth = -1;
            InvalidateMeasure();
            _canvas.Invalidate();
        }
    }

    public (int Start, int End)? Selection { get; set; }

    public (int Start, int End)? Composition { get; set; }

    public IReadOnlyList<CompositionSegment> Segments { get; set; } = [];

    public StyledTextLayout? CurrentLayout => _layout;

    public void Redraw() => _canvas.Invalidate();

    protected override Size MeasureOverride(Size availableSize)
    {
        float width = double.IsInfinity(availableSize.Width) ? 10000 : (float)availableSize.Width;
        if (_layout is null || Math.Abs(width - _layoutWidth) > 0.01)
        {
            _layout?.Dispose();
            _layout = new StyledTextLayout(CanvasDevice.GetSharedDevice(), [new ProtoRun(_text)], Theme, width);
            _layoutWidth = width;
            _canvas.Invalidate();
        }

        var size = new Size(double.IsInfinity(availableSize.Width) ? _layout.Size.X : width, Math.Max(_layout.Size.Y, _layout.Ascent + _layout.Descent));
        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        foreach (var region in args.InvalidatedRegions)
        {
            using var session = sender.CreateDrawingSession(region);
            session.Clear(PageBackground);
            session.TextAntialiasing = CanvasTextAntialiasing.ClearType;
            if (_layout is null)
            {
                continue;
            }

            _layout.Draw(session, Vector2.Zero, Selection, Composition);
            foreach (var segment in Segments)
            {
                if (!segment.ThickUnderline && segment.Kind is not (CompositionSegmentKind.TargetConverted or CompositionSegmentKind.TargetUnconverted))
                {
                    continue;
                }

                int start = Math.Clamp(segment.Start, 0, _text.Length);
                int end = Math.Clamp(segment.End, start, _text.Length);
                foreach (var bounds in _layout.Regions(start, end))
                {
                    float y = (float)(bounds.Y + bounds.Height - 2);
                    session.DrawLine((float)bounds.X, y, (float)(bounds.X + bounds.Width), y, Theme.CompositionUnderline, 2);
                }
            }
        }
    }
}
