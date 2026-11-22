using System.Numerics;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Quill.Playground.TextPrototype;

/// <summary>
/// A block control with self-drawn text (Q62 prototype): measures with <see cref="StyledTextLayout"/>
/// at the available width and draws it on a <see cref="CanvasControl"/>, or on a
/// <see cref="CanvasVirtualControl"/> that only allocates tiles for the parts on screen.
/// </summary>
internal sealed partial class StyledTextView : Grid
{
    private readonly CanvasControl? _canvas;
    private readonly CanvasVirtualControl? _virtual;
    private IReadOnlyList<ProtoRun> _runs = [];
    private StyledTextLayout? _layout;
    private float _layoutWidth = -1;

    public StyledTextView(bool virtualSurface = false)
    {
        if (virtualSurface)
        {
            _virtual = new CanvasVirtualControl();
            _virtual.RegionsInvalidated += OnRegionsInvalidated;
            Children.Add(_virtual);
        }
        else
        {
            _canvas = new CanvasControl();
            _canvas.Draw += OnDraw;
            Children.Add(_canvas);
        }

        Unloaded += (_, _) =>
        {
            _layout?.Dispose();
            _layout = null;
            _layoutWidth = -1;
        };
    }

    public TextTheme Theme { get; set; } = TextTheme.Light;

    public IReadOnlyList<ProtoRun> Runs
    {
        get => _runs;
        set
        {
            _runs = value;
            _layoutWidth = -1;
            InvalidateMeasure();
            Invalidate();
        }
    }

    public (int Start, int End)? Selection { get; set; }

    public (int Start, int End)? Composition { get; set; }

    /// <summary>Draw callbacks so far, for the probes.</summary>
    public int DrawCount { get; private set; }

    public StyledTextLayout? CurrentLayout => _layout;

    protected override Size MeasureOverride(Size availableSize)
    {
        float width = double.IsInfinity(availableSize.Width) ? 10000 : (float)availableSize.Width;
        if (_layout is null || Math.Abs(width - _layoutWidth) > 0.01)
        {
            _layout?.Dispose();
            _layout = new StyledTextLayout(CanvasDevice.GetSharedDevice(), _runs, Theme, width);
            _layoutWidth = width;
            Invalidate();
        }

        var size = new Size(double.IsInfinity(availableSize.Width) ? _layout.Size.X : width, _layout.Size.Y);
        foreach (var child in Children)
        {
            child.Measure(size);
        }

        return size;
    }

    private void Invalidate()
    {
        _canvas?.Invalidate();
        _virtual?.Invalidate();
    }

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args)
    {
        DrawCount++;
        _layout?.Draw(args.DrawingSession, Vector2.Zero, Selection, Composition);
    }

    private void OnRegionsInvalidated(CanvasVirtualControl sender, CanvasRegionsInvalidatedEventArgs args)
    {
        foreach (var region in args.InvalidatedRegions)
        {
            DrawCount++;
            using var session = sender.CreateDrawingSession(region);
            session.Clear(Microsoft.UI.Colors.Transparent);
            _layout?.Draw(session, Vector2.Zero, Selection, Composition);
        }
    }
}
