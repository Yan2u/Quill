using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Quill.VariableXaml;
using Windows.Foundation;

namespace Quill.Playground.SelfTest;

/// <summary>
/// Q7: does an inactive, off-screen window lay out and realize content well enough for layout tests
/// (08-testing-and-debugging.md), without taking the user's foreground? Also probes whether
/// <c>vx:VirtualStack</c> can answer layout queries for blocks outside the viewport.
/// </summary>
internal static class HostWindowProbes
{
    private const double ItemHeight = 30;
    private const double Spacing = 2;

    public static async Task RunAsync(SelfTestContext context)
    {
        Window window = context.Window;
        context.Check(context.HostHandle != 0 && MeasurementHost.ForegroundWindow() != context.HostHandle, "host: the window is not the foreground window");
        context.Info($"host mode: {MeasurementHost.Mode}");
        context.Check(MeasurementHost.Mode != HostMode.OffScreen || MeasurementHost.IsOffScreen(window), "host: an off-screen window lies outside the virtual screen",
            $"position {window.AppWindow.Position.X},{window.AppWindow.Position.Y} size {window.AppWindow.Size.Width}x{window.AppWindow.Size.Height}");
        context.Check(MeasurementHost.IsHiddenFromSwitchers(window), "host: hidden from the taskbar and Alt+Tab");
        context.Check(window.AppWindow.IsVisible, "host: the window is shown (not hidden), so XAML lays it out");

        await ProbeLayoutAsync(context);
        await ProbeRenderingAsync(context);
        await ProbeVirtualStackAsync(context);
    }

    private static async Task ProbeLayoutAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var text = new TextBlock
        {
            Text = string.Concat(Enumerable.Repeat("Wrapping text measured in an inactive window. ", 12)),
            TextWrapping = TextWrapping.Wrap,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, 20, 0, 0),
        };
        context.Stage.Children.Add(text);
        text.UpdateLayout();
        context.Check(text.ActualWidth > 0 && text.ActualHeight > 3 * text.FontSize, "layout: UpdateLayout measures and wraps text",
            $"{text.ActualWidth:F0}x{text.ActualHeight:F0}");

        Point origin = text.TransformToVisual(context.Stage).TransformPoint(new Point(0, 0));
        context.Check(Math.Abs(origin.X - 10) < 0.5 && Math.Abs(origin.Y - 20) < 0.5, "layout: TransformToVisual gives element positions", $"{origin.X:F1},{origin.Y:F1}");

        text.Width = 120;
        double before = text.ActualHeight;
        text.UpdateLayout();
        context.Check(text.ActualHeight > before, "layout: a width change re-wraps synchronously", $"{before:F0} -> {text.ActualHeight:F0}");

        bool loaded = false;
        var late = new Border { Width = 10, Height = 10 };
        late.Loaded += (_, _) => loaded = true;
        context.Stage.Children.Add(late);
        await WaitUntilAsync(() => loaded, 1000);
        context.Check(loaded, "layout: Loaded fires for content added later");
        context.Info($"XamlRoot size {context.Stage.XamlRoot.Size.Width:F0}x{context.Stage.XamlRoot.Size.Height:F0}, rasterization scale {context.Stage.XamlRoot.RasterizationScale:F2}");
        context.Stage.Children.Clear();
    }

    /// <summary>Whether frames are produced for an off-screen window: self-drawn controls (Q8) draw on them.</summary>
    private static async Task ProbeRenderingAsync(SelfTestContext context)
    {
        int frames = 0;
        void OnRendering(object? sender, object e) => frames++;
        CompositionTarget.Rendering += OnRendering;
        context.Stage.Children.Add(new ProgressRing { IsActive = true, Width = 20, Height = 20 });
        await Task.Delay(500);
        CompositionTarget.Rendering -= OnRendering;
        context.Info($"rendering: {frames} CompositionTarget.Rendering callbacks in 500 ms while off-screen");
        context.Stage.Children.Clear();
    }

    private static async Task ProbeVirtualStackAsync(SelfTestContext context)
    {
        context.Stage.Children.Clear();
        var (scroller, repeater) = await CreateStackAsync(context);
        Rect viewport = default;
        repeater.EffectiveViewportChanged += (_, args) => viewport = args.EffectiveViewport;
        int realized = Enumerable.Range(0, 1000).Count(index => repeater.TryGetElement(index) is not null);
        context.Check(realized is > 0 and < 150, "virtual: realization follows the viewport off-screen", $"{realized} realized");

        // Layout rounding to device pixels makes item heights differ from the nominal one at fractional scales.
        double pitch = repeater.TryGetElement(0)!.ActualSize.Y + Spacing;
        context.Info($"virtual: item pitch {pitch:F4} DIP (nominal {ItemHeight + Spacing})");
        await ScrollToAsync(scroller, 500 * pitch);
        context.Check(repeater.TryGetElement(500) is TextBlock { Text: "Item 500" }, "virtual: scrolling realizes the items in view",
            $"offset {scroller.VerticalOffset:F0}, effective viewport {viewport}, realized {RealizedRange(repeater)}");

        await ScrollToAsync(scroller, 700 * pitch);
        UIElement? scrolled = repeater.TryGetElement(700);
        Point exact = scrolled?.TransformToVisual(repeater).TransformPoint(new Point(0, 0)) ?? new Point(double.NaN, double.NaN);
        context.Check(scrolled is TextBlock { Text: "Item 700" } && Math.Abs(exact.Y - 700 * pitch) < 1,
            "virtual: after scrolling to a block its position is exact", $"y={exact.Y:F0}, expected {700 * pitch:F0}");

        await ScrollToAsync(scroller, 0);
        context.Check(repeater.TryGetElement(0) is TextBlock { Text: "Item 0" }, "virtual: scrolling back realizes the first items again");
        context.Stage.Children.Clear();

        // GetOrCreateElement for a block far outside the viewport: the block is measured, but the realization
        // anchor moves to it, so later scrolling realizes the wrong items.
        var (farScroller, farRepeater) = await CreateStackAsync(context);
        UIElement far = farRepeater.GetOrCreateElement(700);
        farRepeater.UpdateLayout();
        Point estimated = far.TransformToVisual(farRepeater).TransformPoint(new Point(0, 0));
        context.Check(far is TextBlock { Text: "Item 700" } && Math.Abs(far.ActualSize.Y - ItemHeight) < 0.5,
            "virtual: GetOrCreateElement measures a block outside the viewport", $"height {far.ActualSize.Y:F1}");
        context.Info($"virtual: its position y={estimated.Y:F0} (exact {700 * pitch:F0}; items of one height hide any estimation error)");
        await ScrollToAsync(farScroller, 500 * pitch);
        context.Info($"virtual: after it, scrolling to item 500 realizes {RealizedRange(farRepeater)} (without it: around 482-521)");
        await ScrollToAsync(farScroller, 0);
        context.Info($"virtual: scrolling back to the top realizes {RealizedRange(farRepeater)}; element 700 still realized={ReferenceEquals(farRepeater.TryGetElement(700), far)}");
        context.Stage.Children.Clear();
    }

    private static async Task<(ScrollViewer Scroller, ItemsRepeater Repeater)> CreateStackAsync(SelfTestContext context)
    {
        var host = new VariableXamlRoot { DeferUpdates = false, Height = 400, VerticalAlignment = VerticalAlignment.Top };
        context.Stage.Children.Add(host);
        string items = string.Concat(Enumerable.Range(0, 1000).Select(i => $"<TextBlock vx:Key=\"p{i}\" Text=\"Item {i}\" Height=\"{ItemHeight}\"/>"));
        host.Xaml = $"<ScrollViewer><vx:VirtualStack Spacing=\"{Spacing}\">{items}</vx:VirtualStack></ScrollViewer>";
        await Task.Delay(300);
        var scroller = (ScrollViewer)host.Content;
        return (scroller, (ItemsRepeater)scroller.Content);
    }

    /// <summary>Scrolls without animation and waits until the offset has settled (or a timeout).</summary>
    private static async Task ScrollToAsync(ScrollViewer scroller, double offset)
    {
        scroller.ChangeView(null, offset, null, disableAnimation: true);
        await WaitUntilAsync(() => Math.Abs(scroller.VerticalOffset - Math.Min(offset, scroller.ScrollableHeight)) < 1, 2000);
        await Task.Delay(100);
    }

    private static string RealizedRange(ItemsRepeater repeater)
    {
        var indices = Enumerable.Range(0, 1000).Where(index => repeater.TryGetElement(index) is not null).ToList();
        return indices.Count == 0 ? "none" : $"{indices.Count} items in [{indices[0]}, {indices[^1]}]";
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMilliseconds)
    {
        for (int waited = 0; waited < timeoutMilliseconds && !condition(); waited += 20)
        {
            await Task.Delay(20);
        }
    }
}
